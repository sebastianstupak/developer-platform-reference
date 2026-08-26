using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.ServiceAccounts.CreateServiceAccount;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.ApiKeys;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class ServiceAccountEscalationGuardTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();   // the caller (execution context principal)

    public async Task InitializeAsync()
    {
        var ctx = new Ctx { TenantId = _tenant, PrincipalId = _actor };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        _ctx = ctx;
    }
    private Ctx _ctx = null!;
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CreateServiceAccountCommandHandler Handler() =>
        new(_db, _ctx, new PrivilegeGuard(new AuthorizationService(_db), _db));

    [Fact]
    public async Task CreateServiceAccount_Denied_When_Actor_Lacks_Grant_Permission()
    {
        var grants = new[] { new GrantSpec(Permission.SecretsWrite, ScopeType.Tenant, null) };

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Handler().HandleAsync(new CreateServiceAccountCommand("sa-1", null, grants)));

        Assert.False(await _db.Principals.AsNoTracking().AnyAsync());
        Assert.False(await _db.PermissionGrants.AsNoTracking().AnyAsync());
    }

    [Fact]
    public async Task CreateServiceAccount_Succeeds_And_Persists_Grants_When_Actor_Holds_Them()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var grants = new[] { new GrantSpec(Permission.SecretsWrite, ScopeType.Tenant, null) };
        var result = await Handler().HandleAsync(new CreateServiceAccountCommand("sa-1", null, grants));

        var grant = await _db.PermissionGrants.AsNoTracking()
            .SingleAsync(g => g.PrincipalId == result.ServiceAccountId);
        Assert.Equal(Permission.SecretsWrite, grant.Permission);
    }

    private sealed class Ctx : IExecutionContext
    {
        public Guid TenantId { get; set; }
        public Guid? PrincipalId { get; set; }
        public PrincipalType? PrincipalType => Domain.Authorization.PrincipalType.Member;
        public Guid? UserId => null;
        public Guid? ProjectId => null;
        public Guid? EnvironmentId => null;
        public string IpAddress => "127.0.0.1";
        public bool IsCrossTenantOperation { get; set; }
    }
}
