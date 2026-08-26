using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RevealSecret;
using DeveloperPlatform.Application.Secrets.SetSecret;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Api.Tests.Secrets;

public class SecretAuthorizationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private HttpExecutionContext _ctx = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        _ctx = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = PrincipalType.Member };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, _ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CommandDispatcher Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<SetSecretCommand, Unit>, SetSecretCommandHandler>();
        services.AddScoped<ICommandHandler<RevealSecretCommand, RevealSecretResult>, RevealSecretCommandHandler>();
        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped(_ => _db);
        services.AddScoped<IExecutionContext>(_ => _ctx);
        services.AddScoped<ITenantCryptoService>(_ => new TenantCryptoService(_db, Key));
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        return new CommandDispatcher(sp, _db, _ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
    }

    [Fact]
    public async Task Set_Allowed_With_Environment_Grant()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        await Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v"));
        Assert.Single(await _db.Secrets.ToListAsync());
    }

    [Fact]
    public async Task Set_Forbidden_Without_Grant()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v")));
    }

    [Fact]
    public async Task Set_Audit_Redacts_Value()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        await Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "supersecret"));

        var entry = await _db.AuditOutboxEntries.AsNoTracking().SingleAsync();
        var plaintext = await new TenantCryptoService(_db, Key).DecryptAsync(_tenant, entry.EncryptedPayload, entry.KeyId);
        Assert.DoesNotContain("supersecret", plaintext);
        Assert.Contains("[REDACTED]", plaintext);
    }

    [Fact]
    public async Task Reveal_Writes_Audit_Entry()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsRead, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        await Build().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v"));
        await Build().SendAsync<RevealSecretCommand, RevealSecretResult>(new RevealSecretCommand(_project, _env, "K"));

        var types = await _db.AuditOutboxEntries.AsNoTracking().Select(e => e.CommandType).ToListAsync();
        Assert.Contains(nameof(RevealSecretCommand), types);
    }
}
