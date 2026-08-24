using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Dispatching;

public class CommandDispatcherTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private CommandDispatcher _dispatcher = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private static readonly byte[] MasterKey =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        var ctx = new HttpExecutionContext
        {
            TenantId = _tenantId,
            IpAddress = "127.0.0.1"
        };

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();

        var crypto = new TenantCryptoService(_db, MasterKey);
        await crypto.CreateKeyAsync(_tenantId);
        await _db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommand, Guid>, TestCommandHandler>();
        services.AddScoped<ICommandHandler<SkippedCommand, Unit>, SkippedCommandHandler>();
        var sp = services.BuildServiceProvider();

        var repo = new AuditOutboxRepository(_db);
        var scrubber = new SensitiveDataScrubber();
        _dispatcher = new CommandDispatcher(sp, _db, ctx, crypto, repo, scrubber, TenancyMode.SharedTables);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Dispatch_Writes_AuditOutboxEntry_On_Success()
    {
        var command = new TestCommand("test-name", "secret-value");

        await _dispatcher.SendAsync<TestCommand, Guid>(command);

        var entry = await _db.AuditOutboxEntries.SingleAsync();
        Assert.Equal(_tenantId, entry.TenantId);
        Assert.Equal(AuditStatus.Success, entry.Status);
        Assert.Equal(nameof(TestCommand), entry.CommandType);
    }

    [Fact]
    public async Task Dispatch_Scrubs_SensitiveData_In_Outbox_Payload()
    {
        var crypto = new TenantCryptoService(_db, MasterKey);
        var command = new TestCommand("test-name", "my-secret");

        await _dispatcher.SendAsync<TestCommand, Guid>(command);

        var entry = await _db.AuditOutboxEntries.SingleAsync();
        var decrypted = await crypto.DecryptAsync(_tenantId, entry.EncryptedPayload, entry.KeyId);

        Assert.Contains("[REDACTED]", decrypted);
        Assert.DoesNotContain("my-secret", decrypted);
    }

    [Fact]
    public async Task Dispatch_Skips_Audit_When_SkipAudit_Attribute()
    {
        var command = new SkippedCommand();

        await _dispatcher.SendAsync<SkippedCommand, Unit>(command);

        Assert.Empty(_db.AuditOutboxEntries);
    }

    [Fact]
    public async Task Dispatch_CrossTenant_Throws_In_DatabasePerTenant_Mode()
    {
        var ctx = new HttpExecutionContext { TenantId = _tenantId, IpAddress = "127.0.0.1" };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ApplicationDbContext(options, ctx, TenancyMode.DatabasePerTenant);
        await db.Database.EnsureCreatedAsync();

        var crypto = new TenantCryptoService(db, MasterKey);
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<CrossTenantCommand, Unit>, CrossTenantCommandHandler>();
        var sp = services.BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp, db, ctx, crypto,
            new AuditOutboxRepository(db), new SensitiveDataScrubber(), TenancyMode.DatabasePerTenant);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => dispatcher.SendAsync<CrossTenantCommand, Unit>(new CrossTenantCommand()));
    }

    // --- Test doubles ---

    public record TestCommand(string Name, [property: SensitiveData] string SecretValue)
        : ICommand<Guid>;

    public class TestCommandHandler : ICommandHandler<TestCommand, Guid>
    {
        public Task<Guid> HandleAsync(TestCommand command, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());
    }

    [SkipAudit]
    public record SkippedCommand : ICommand;

    public class SkippedCommandHandler : ICommandHandler<SkippedCommand, Unit>
    {
        public Task<Unit> HandleAsync(SkippedCommand command, CancellationToken ct = default)
            => Task.FromResult(Unit.Value);
    }

    [CrossTenant(Reason = "System-level operation")]
    public record CrossTenantCommand : ICommand;

    public class CrossTenantCommandHandler : ICommandHandler<CrossTenantCommand, Unit>
    {
        public Task<Unit> HandleAsync(CrossTenantCommand command, CancellationToken ct = default)
            => Task.FromResult(Unit.Value);
    }
}
