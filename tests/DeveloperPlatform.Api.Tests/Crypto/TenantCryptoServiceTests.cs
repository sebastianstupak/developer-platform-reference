using DeveloperPlatform.Infrastructure.Crypto;
using Microsoft.EntityFrameworkCore;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;

namespace DeveloperPlatform.Api.Tests.Crypto;

public class TenantCryptoServiceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private TenantCryptoService _sut = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    // 32-byte master key for tests
    private static readonly byte[] MasterKey =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Minimal no-op execution context for tests
        var ctx = new TestExecutionContext { TenantId = _tenantId };
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();

        _sut = new TenantCryptoService(_db, MasterKey);

        await _sut.CreateKeyAsync(_tenantId);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task EncryptThenDecrypt_Returns_Original_Plaintext()
    {
        var plaintext = """{"command":"CreateApiKey","name":"my-key"}""";

        var (encrypted, keyId) = await _sut.EncryptAsync(_tenantId, plaintext);
        var decrypted = await _sut.DecryptAsync(_tenantId, encrypted, keyId);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public async Task Decrypt_Throws_After_KeyShredded()
    {
        var plaintext = "sensitive payload";
        var (encrypted, keyId) = await _sut.EncryptAsync(_tenantId, plaintext);
        await _db.SaveChangesAsync();

        await _sut.ShredKeyAsync(_tenantId);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DecryptAsync(_tenantId, encrypted, keyId));
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId => null;
        public DeveloperPlatform.Domain.Authorization.PrincipalType? PrincipalType => null;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
