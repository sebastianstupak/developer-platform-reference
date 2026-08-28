using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Secrets;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class SecretTests : IAsyncLifetime
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
    public void SetNewVersion_Advances_Version_And_Value()
    {
        var s = Secret.Create(_tenant, _project, _env, "DB_URL", new byte[] { 1 }, Guid.NewGuid());
        Assert.Equal(1, s.CurrentVersion);
        var created = s.UpdatedAt;
        s.SetNewVersion(new byte[] { 2 }, Guid.NewGuid());
        Assert.Equal(2, s.CurrentVersion);
        Assert.True(s.UpdatedAt >= created);
        Assert.Equal(2, s.EncryptedValue[0]);
    }

    [Fact]
    public void ReEncryptCurrent_Changes_Value_But_Not_Version()
    {
        var s = Secret.Create(_tenant, _project, _env, "DB_URL", new byte[] { 1 }, Guid.NewGuid());
        s.ReEncryptCurrent(new byte[] { 9 }, Guid.NewGuid());
        Assert.Equal(1, s.CurrentVersion);
        Assert.Equal(9, s.EncryptedValue[0]);
    }

    [Fact]
    public void SecretVersion_Create_Records_Fields()
    {
        var secretId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var v = SecretVersion.Create(_tenant, secretId, 3, new byte[] { 7 }, Guid.NewGuid(),
            principalId: Guid.NewGuid(), principalType: "Member", userId: userId, rolledBackFrom: 1);
        Assert.Equal(secretId, v.SecretId);
        Assert.Equal(3, v.VersionNumber);
        Assert.Equal(7, v.EncryptedValue[0]);
        Assert.Equal("Member", v.CreatedByPrincipalType);
        Assert.Equal(userId, v.CreatedByUserId);
        Assert.Equal(1, v.RolledBackFrom);
    }

    [Fact]
    public async Task SecretVersions_Persist_And_Query_By_Secret_Newest_First()
    {
        var secretId = Guid.NewGuid();
        _db.Add(SecretVersion.Create(_tenant, secretId, 1, new byte[] { 1 }, Guid.NewGuid(), null, null, null));
        _db.Add(SecretVersion.Create(_tenant, secretId, 2, new byte[] { 2 }, Guid.NewGuid(), null, null, null));
        await _db.SaveChangesAsync();

        var rows = await _db.SecretVersions.AsNoTracking()
            .Where(v => v.SecretId == secretId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();

        Assert.Equal(new[] { 2, 1 }, rows.Select(v => v.VersionNumber));
    }

    [Fact]
    public async Task Repository_Get_By_Environment_And_Name()
    {
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        await repo.AddAsync(Secret.Create(_tenant, _project, _env, "TOKEN", new byte[] { 9 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        Assert.NotNull(await repo.GetAsync(_env, "TOKEN"));
        Assert.Null(await repo.GetAsync(_env, "MISSING"));
    }

    [Fact]
    public async Task Set_Then_Set_Overwrites_And_Encrypts()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
            new TestExecutionContext { TenantId = _tenant });

        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "first"));
        await _db.SaveChangesAsync();
        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "second"));
        await _db.SaveChangesAsync();

        var stored = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("second", System.Text.Encoding.UTF8.GetString(stored.EncryptedValue));
        Assert.Equal("second", await crypto.DecryptAsync(_tenant, stored.EncryptedValue, stored.KeyId));
    }

    [Fact]
    public async Task List_Returns_Names_And_Meta_Only()
    {
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "A", new byte[] { 1 }, Guid.NewGuid()));
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "B", new byte[] { 2 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.ListSecretsQueryHandler(_db);
        var list = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.ListSecrets.ListSecretsQuery(_project, _env));
        Assert.Equal(new[] { "A", "B" }, list.Select(s => s.Name));
    }

    [Fact]
    public async Task Reveal_Returns_Original_Plaintext()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var (payload, keyId) = await crypto.EncryptAsync(_tenant, "sesame");
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "PW", payload, keyId));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
            new TestExecutionContext { TenantId = _tenant });
        var result = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.RevealSecret.RevealSecretCommand(_project, _env, "PW"));
        Assert.Equal("sesame", result.Value);
    }

    [Fact]
    public async Task Delete_Removes_Secret_And_404_When_Absent()
    {
        _db.Secrets.Add(Secret.Create(_tenant, _project, _env, "X", new byte[] { 1 }, Guid.NewGuid()));
        await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.DeleteSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db));
        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.DeleteSecret.DeleteSecretCommand(_project, _env, "X"));
        await _db.SaveChangesAsync();
        Assert.Empty(await _db.Secrets.ToListAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(new DeveloperPlatform.Application.Secrets.DeleteSecret.DeleteSecretCommand(_project, _env, "X")));
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
