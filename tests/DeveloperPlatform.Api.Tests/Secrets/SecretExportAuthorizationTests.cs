using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.ExportSecrets;
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

public class SecretExportAuthorizationTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _env = Guid.NewGuid();
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public async Task InitializeAsync()
    {
        var boot = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = PrincipalType.Member };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        _db = new ApplicationDbContext(options, boot, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        var crypto = new TenantCryptoService(_db, Key);
        await crypto.CreateKeyAsync(_tenant);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // Build a dispatcher bound to a principal of the given type. Authorization resolves by
    // principal id + scope (type-agnostic), so this exercises both the Member and the
    // ServiceAccount (API-key) principal through the identical gate.
    private (CommandDispatcher Dispatcher, HttpExecutionContext Ctx) Build(PrincipalType type)
    {
        var ctx = new HttpExecutionContext { TenantId = _tenant, IpAddress = "127.0.0.1", PrincipalId = _principal, PrincipalType = type };
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<ExportSecretsCommand, ExportSecretsResult>, ExportSecretsCommandHandler>();
        services.AddScoped<ICommandHandler<SetSecretCommand, Unit>, SetSecretCommandHandler>();
        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped(_ => _db);
        services.AddScoped<IExecutionContext>(_ => ctx);
        services.AddScoped<ITenantCryptoService>(_ => new TenantCryptoService(_db, Key));
        var sp = services.BuildServiceProvider();
        var authz = new DeveloperPlatform.Infrastructure.Authorization.AuthorizationService(_db);
        var dispatcher = new CommandDispatcher(sp, _db, ctx, new TenantCryptoService(_db, Key),
            new AuditOutboxRepository(_db), new SensitiveDataScrubber(), TenancyMode.SharedTables, authz);
        return (dispatcher, ctx);
    }

    private async Task GrantReadAsync()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsRead, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_Forbidden_Without_Grant()
    {
        var (d, _) = Build(PrincipalType.Member);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env)));
    }

    [Fact]
    public async Task Export_Allowed_For_Member_With_SecretsRead()
    {
        await GrantReadAsync();
        // Seed a secret via a write-capable member so there is something to export.
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _principal, Permission.SecretsWrite, Scope.Environment(_env)));
        await _db.SaveChangesAsync();
        var (d, _) = Build(PrincipalType.Member);
        await d.SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(_project, _env, "K", "v"));

        var result = await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env));
        Assert.Equal("v", result.Secrets["K"]);
    }

    [Fact]
    public async Task Export_Allowed_For_ServiceAccount_With_SecretsRead()
    {
        await GrantReadAsync();
        // Seed a secret directly (crypto + repository) so no write grant is needed.
        var crypto = new TenantCryptoService(_db, Key);
        var (payload, keyId) = await crypto.EncryptAsync(_tenant, "machine-value");
        _db.Secrets.Add(DeveloperPlatform.Domain.Secrets.Secret.Create(_tenant, _project, _env, "M", payload, keyId));
        await _db.SaveChangesAsync();

        var (d, _) = Build(PrincipalType.ServiceAccount);
        var result = await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env));
        Assert.Equal("machine-value", result.Secrets["M"]);
    }

    [Fact]
    public async Task Export_Forbidden_For_ServiceAccount_Without_Grant()
    {
        var (d, _) = Build(PrincipalType.ServiceAccount);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env)));
    }

    [Fact]
    public async Task Export_Writes_Audit_Entry()
    {
        await GrantReadAsync();
        var (d, _) = Build(PrincipalType.ServiceAccount);
        await d.SendAsync<ExportSecretsCommand, ExportSecretsResult>(new ExportSecretsCommand(_project, _env));

        var types = await _db.AuditOutboxEntries.AsNoTracking().Select(e => e.CommandType).ToListAsync();
        Assert.Contains(nameof(ExportSecretsCommand), types);
    }
}
