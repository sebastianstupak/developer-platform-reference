using DeveloperPlatform.Application.ApiKeys.CreateApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Infrastructure.ApiKeys;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.ApiKeys;

public class CreateApiKeyTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private CommandDispatcher _dispatcher = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();
    private static readonly byte[] MasterKey =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        var ctx = new HttpExecutionContext
        {
            TenantId = _tenantId,
            ProjectId = _projectId,
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
        services.AddScoped<ICommandHandler<CreateApiKeyCommand, CreateApiKeyResult>>(
            _ => new CreateApiKeyCommandHandler(new ApiKeyRepository(_db), ctx));
        var sp = services.BuildServiceProvider();

        _dispatcher = new CommandDispatcher(
            sp, _db, ctx, crypto,
            new AuditOutboxRepository(_db),
            new SensitiveDataScrubber(),
            TenancyMode.SharedTables,
            new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db));
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task CreateApiKey_Persists_Key_And_Writes_Outbox()
    {
        var command = new CreateApiKeyCommand(
            _projectId, null, "My Integration Key", ApiKeyScope.Read | ApiKeyScope.Write, null);

        var result = await _dispatcher.SendAsync<CreateApiKeyCommand, CreateApiKeyResult>(command);

        Assert.NotEqual(Guid.Empty, result.ApiKeyId);
        Assert.StartsWith("dpk_", result.PlaintextKey);

        var persisted = await _db.ApiKeys.SingleAsync();
        Assert.Equal(_tenantId, persisted.TenantId);
        Assert.Equal(_projectId, persisted.ProjectId);
        Assert.False(persisted.IsRevoked);

        var outbox = await _db.AuditOutboxEntries.SingleAsync();
        Assert.Equal(nameof(CreateApiKeyCommand), outbox.CommandType);
    }

    [Fact]
    public async Task CreateApiKey_Redacts_PlaintextKey_In_Audit_Payload()
    {
        var command = new CreateApiKeyCommand(
            _projectId, null, "Secret Key", ApiKeyScope.Read, null);

        await _dispatcher.SendAsync<CreateApiKeyCommand, CreateApiKeyResult>(command);

        var outbox = await _db.AuditOutboxEntries.SingleAsync();
        var crypto = new TenantCryptoService(_db, MasterKey);
        var decrypted = await crypto.DecryptAsync(_tenantId, outbox.EncryptedPayload, outbox.KeyId);

        Assert.Contains("[REDACTED]", decrypted);
    }
}
