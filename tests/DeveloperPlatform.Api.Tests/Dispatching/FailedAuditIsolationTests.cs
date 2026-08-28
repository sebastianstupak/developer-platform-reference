using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Dispatching;

// Regression coverage for: a failed command handler that mutated tracked entities must not
// have those mutations flushed when the dispatcher writes the failure audit entry. The audit
// write reuses the same ApplicationDbContext, so without clearing the change tracker first,
// SaveChangesAsync on the audit path would also persist the failed handler's still-tracked
// (Modified) entities — silently un-rolling-back a "rolled back" transaction.
public class FailedAuditIsolationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private HttpExecutionContext _ctx = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private static readonly byte[] MasterKey =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        _ctx = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1" };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();

        var crypto = new TenantCryptoService(_db, MasterKey);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CommandDispatcher Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<FailingMutationCommand, Unit>>(
            _ => new FailingMutationCommandHandler(_db));
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        return new CommandDispatcher(sp, _db, _ctx, new TenantCryptoService(_db, MasterKey),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
    }

    [Fact]
    public async Task Failed_Command_Does_Not_Persist_Rolled_Back_Entity_Mutations()
    {
        var originalValue = new byte[] { 1, 2, 3 };
        var secret = Secret.Create(_tenant, Guid.NewGuid(), Guid.NewGuid(), "API_KEY", originalValue, Guid.NewGuid());
        _db.Secrets.Add(secret);
        await _db.SaveChangesAsync();

        var command = new FailingMutationCommand(secret.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build().SendAsync<FailingMutationCommand, Unit>(command));

        // Read what was actually persisted — must reflect the pre-mutation value, i.e. the
        // "rolled back" mutation must never have been flushed by the failed-audit write.
        var persisted = await _db.Secrets.AsNoTracking().SingleAsync(s => s.Id == secret.Id);
        Assert.Equal(originalValue, persisted.EncryptedValue);

        var failedEntries = await _db.AuditOutboxEntries.AsNoTracking()
            .Where(e => e.Status == AuditStatus.Failed)
            .ToListAsync();
        Assert.Single(failedEntries);
    }

    // --- Test doubles ---

    public record FailingMutationCommand(Guid SecretId) : ICommand;

    public class FailingMutationCommandHandler(ApplicationDbContext db) : ICommandHandler<FailingMutationCommand, Unit>
    {
        public async Task<Unit> HandleAsync(FailingMutationCommand command, CancellationToken ct = default)
        {
            var secret = await db.Secrets.FindAsync([command.SecretId], ct)
                ?? throw new InvalidOperationException("secret not found");
            secret.SetNewVersion(new byte[] { 9, 9, 9 }, Guid.NewGuid());
            throw new InvalidOperationException("boom");
        }
    }
}
