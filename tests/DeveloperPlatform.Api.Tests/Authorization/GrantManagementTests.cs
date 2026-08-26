using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Grants.AssignRole;
using DeveloperPlatform.Application.Grants.GrantPermission;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Members;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class GrantManagementTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();   // the caller (execution context principal)
    private readonly Guid _target = Guid.NewGuid();   // the principal being granted to

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

    private GrantPermissionCommandHandler GrantHandler() =>
        new(_db, _ctx, new PrivilegeGuard(new AuthorizationService(_db), _db));

    [Fact]
    public async Task GrantPermission_Denied_When_Actor_Lacks_It()
    {
        await Assert.ThrowsAsync<DeveloperPlatform.Application.Authorization.ForbiddenException>(
            () => GrantHandler().HandleAsync(
                new GrantPermissionCommand(_target, Permission.SecretsWrite, ScopeType.Tenant, null)));
    }

    [Fact]
    public async Task GrantPermission_Succeeds_And_Persists_When_Actor_Holds_It()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var result = await GrantHandler().HandleAsync(
            new GrantPermissionCommand(_target, Permission.SecretsWrite, ScopeType.Tenant, null));

        var grant = await _db.PermissionGrants.AsNoTracking().SingleAsync(g => g.Id == result.GrantId);
        Assert.Equal(_target, grant.PrincipalId);
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
