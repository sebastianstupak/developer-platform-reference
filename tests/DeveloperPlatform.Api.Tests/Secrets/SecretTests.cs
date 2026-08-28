using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
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
    public async Task Set_New_Secret_Writes_Version_1()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var handler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(
            new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db), crypto,
            new TestExecutionContext { TenantId = _tenant });

        await handler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "API_KEY", "first"));
        await _db.SaveChangesAsync();

        var secret = await _db.Secrets.AsNoTracking().SingleAsync();
        var versions = await _db.SecretVersions.AsNoTracking().Where(v => v.SecretId == secret.Id).ToListAsync();
        Assert.Equal(1, secret.CurrentVersion);
        var v1 = Assert.Single(versions);
        Assert.Equal(1, v1.VersionNumber);
        Assert.Null(v1.RolledBackFrom);
        Assert.Equal("first", await crypto.DecryptAsync(_tenant, v1.EncryptedValue, v1.KeyId));
    }

    [Fact]
    public async Task Set_Twice_Writes_Version_2_And_Advances_Current()
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

        var secret = await _db.Secrets.AsNoTracking().SingleAsync();
        var versions = await _db.SecretVersions.AsNoTracking()
            .Where(v => v.SecretId == secret.Id).OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal(2, secret.CurrentVersion);
        Assert.Equal(new[] { 1, 2 }, versions.Select(v => v.VersionNumber));
        Assert.Equal("first", await crypto.DecryptAsync(_tenant, versions[0].EncryptedValue, versions[0].KeyId));
        Assert.Equal("second", await crypto.DecryptAsync(_tenant, versions[1].EncryptedValue, versions[1].KeyId));
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

    [Fact]
    public async Task Delete_Secret_Also_Removes_Its_Versions()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        var setHandler = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto,
            new TestExecutionContext { TenantId = _tenant });
        await setHandler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "X", "v1"));
        await _db.SaveChangesAsync();
        await setHandler.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "X", "v2"));
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();   // simulate a fresh request scope: with no tracked SecretVersion entities, EF's tracked-entity cascade can't mask the explicit removal (InMemory enforces no FK cascade)

        var delHandler = new DeveloperPlatform.Infrastructure.Secrets.DeleteSecretCommandHandler(repo);
        await delHandler.HandleAsync(new DeveloperPlatform.Application.Secrets.DeleteSecret.DeleteSecretCommand(_project, _env, "X"));
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.Secrets.ToListAsync());
        Assert.Empty(await _db.SecretVersions.ToListAsync());
    }

    [Fact]
    public async Task ListVersions_Returns_Newest_First_With_Current_And_Actor()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();

        var user = User.Create("kc-sub-1", "alice@example.com", "Alice");
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var ctx = new TestExecutionContext { TenantId = _tenant };
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);

        // Two versions; the second authored by a known Member user.
        var (p1, k1) = await crypto.EncryptAsync(_tenant, "one");
        var secret = Secret.Create(_tenant, _project, _env, "TOKEN", p1, k1);
        await repo.AddAsync(secret);
        await repo.AddVersionAsync(SecretVersion.Create(_tenant, secret.Id, 1, p1, k1, null, null, null));
        var (p2, k2) = await crypto.EncryptAsync(_tenant, "two");
        secret.SetNewVersion(p2, k2);
        await repo.AddVersionAsync(SecretVersion.Create(_tenant, secret.Id, 2, p2, k2,
            principalId: Guid.NewGuid(), principalType: "Member", userId: user.Id));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.ListSecretVersionsQueryHandler(_db);
        var list = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.ListSecretVersions.ListSecretVersionsQuery(_project, _env, "TOKEN"));

        Assert.Equal(new[] { 2, 1 }, list.Select(v => v.VersionNumber));
        Assert.True(list[0].IsCurrent);
        Assert.False(list[1].IsCurrent);
        Assert.Equal("alice@example.com", list[0].Actor);
    }

    [Fact]
    public async Task RevealVersion_Returns_That_Versions_Plaintext()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
        await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
        await _db.SaveChangesAsync();
        await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "two"));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretVersionCommandHandler(repo, crypto, ctx);
        var v1 = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.RevealSecretVersion.RevealSecretVersionCommand(_project, _env, "K", 1));
        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal("one", v1.Value);
    }

    [Fact]
    public async Task RevealVersion_Still_Works_After_Key_Rotation()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
        await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
        await _db.SaveChangesAsync();

        // Rotate the tenant key: re-encrypts current values, retains the old key.
        var rotate = new DeveloperPlatform.Infrastructure.Secrets.RotateTenantKeyCommandHandler(_db, crypto, ctx);
        await rotate.HandleAsync(new DeveloperPlatform.Application.Secrets.RotateTenantKey.RotateTenantKeyCommand());
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretVersionCommandHandler(repo, crypto, ctx);
        var v1 = await handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.RevealSecretVersion.RevealSecretVersionCommand(_project, _env, "K", 1));
        Assert.Equal("one", v1.Value);

        // Rotation must not create a new version.
        var secret = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(1, secret.CurrentVersion);
        Assert.Single(await _db.SecretVersions.AsNoTracking().Where(v => v.SecretId == secret.Id).ToListAsync());
    }

    [Fact]
    public async Task RevealVersion_Unknown_Version_Throws()
    {
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
        var repo = new DeveloperPlatform.Infrastructure.Secrets.SecretRepository(_db);
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var set = new DeveloperPlatform.Infrastructure.Secrets.SetSecretCommandHandler(repo, crypto, ctx);
        await set.HandleAsync(new DeveloperPlatform.Application.Secrets.SetSecret.SetSecretCommand(_project, _env, "K", "one"));
        await _db.SaveChangesAsync();

        var handler = new DeveloperPlatform.Infrastructure.Secrets.RevealSecretVersionCommandHandler(repo, crypto, ctx);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
            new DeveloperPlatform.Application.Secrets.RevealSecretVersion.RevealSecretVersionCommand(_project, _env, "K", 99)));
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
