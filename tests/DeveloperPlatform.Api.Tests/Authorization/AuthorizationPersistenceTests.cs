using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class AuthorizationPersistenceTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenantId };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task System_Roles_Are_Seeded_With_Their_Permissions()
    {
        var roles = await _db.Roles.AsNoTracking().ToListAsync();
        Assert.Equal(4, roles.Count);
        Assert.All(roles, r => Assert.True(r.IsSystem));

        var ownerPerms = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == SystemRoles.OwnerId).ToListAsync();
        Assert.Equal(Enum.GetValues<Permission>().Length, ownerPerms.Count);

        var viewerPerms = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == SystemRoles.ViewerId).Select(rp => rp.Permission).ToListAsync();
        Assert.Contains(Permission.ProjectsRead, viewerPerms);
        Assert.DoesNotContain(Permission.ProjectsWrite, viewerPerms);
    }

    [Fact]
    public async Task Member_Principal_And_Grant_RoundTrip()
    {
        var user = User.Create("kc-1", "dev@example.com", "Dev");
        _db.Users.Add(user);
        var principal = Principal.CreateMember(_tenantId, "Dev");
        _db.Principals.Add(principal);
        _db.Memberships.Add(Membership.Create(_tenantId, principal.Id, user.Id, MembershipStatus.Active));
        _db.PermissionGrants.Add(
            PermissionGrant.Create(_tenantId, principal.Id, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();

        var grant = await _db.PermissionGrants.AsNoTracking().SingleAsync();
        Assert.Equal(principal.Id, grant.PrincipalId);
        Assert.Equal(Permission.SecretsWrite, grant.Permission);
        Assert.Equal(ScopeType.Tenant, grant.ScopeType);
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
