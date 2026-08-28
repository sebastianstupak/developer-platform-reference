using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecretVersions;
using DeveloperPlatform.Application.Secrets.RevealSecretVersion;
using DeveloperPlatform.Application.Secrets.RollbackSecret;
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

public class SecretVersioningAuthorizationTests : IAsyncLifetime
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

    private CommandDispatcher BuildCommands()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<SetSecretCommand, Unit>, SetSecretCommandHandler>();
        services.AddScoped<ICommandHandler<RollbackSecretCommand, Unit>, RollbackSecretCommandHandler>();
        services.AddScoped<ICommandHandler<RevealSecretVersionCommand, RevealSecretVersionResult>, RevealSecretVersionCommandHandler>();
        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped(_ => _db);
        services.AddScoped<IExecutionContext>(_ => _ctx);
        services.AddScoped<ITenantCryptoService>(_ => new TenantCryptoService(_db, Key));
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        return new CommandDispatcher(sp, _db, _ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
    }

    private async Task GrantAsync(Permission permission)
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, permission, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollback_Forbidden_Without_SecretsWrite()
    {
        await GrantAsync(Permission.SecretsWrite);
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "one"));
        // Revoke by starting a fresh principal grant set: remove write grant.
        _db.PermissionGrants.RemoveRange(_db.PermissionGrants);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            BuildCommands().SendAsync<RollbackSecretCommand, Unit>(new RollbackSecretCommand(_project, _env, "K", 1)));
    }

    [Fact]
    public async Task Rollback_Allowed_With_SecretsWrite()
    {
        await GrantAsync(Permission.SecretsWrite);
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "one"));
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "two"));
        await BuildCommands().SendAsync<RollbackSecretCommand, Unit>(new RollbackSecretCommand(_project, _env, "K", 1));

        var secret = await _db.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(3, secret.CurrentVersion);
    }

    [Fact]
    public async Task RevealVersion_Forbidden_Without_SecretsRead()
    {
        await GrantAsync(Permission.SecretsWrite);
        await BuildCommands().SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "one"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            BuildCommands().SendAsync<RevealSecretVersionCommand, RevealSecretVersionResult>(
                new RevealSecretVersionCommand(_project, _env, "K", 1)));
    }
}
