using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class RotationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Rotate_ReEncrypts_All_Secrets_To_New_Key_And_Values_Preserved()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var (p1, k1) = await crypto.EncryptAsync(_tenant, "one");
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "A", p1, k1));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RotateTenantKeyCommandHandler(
            _db, crypto, new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.RotateTenantKey.RotateTenantKeyCommand());
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.SecretsReEncrypted);
        var s = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.NotEqual(k1, s.KeyId);                                   // new key
        Assert.Equal("one", await crypto.DecryptAsync(_tenant, s.EncryptedValue, s.KeyId));  // value preserved
        Assert.Equal("one", await crypto.DecryptAsync(_tenant, p1, k1)); // old key retained → still decrypts
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId => null;
        public PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
