using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Infrastructure.Authorization;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PrivilegeGuardTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private PrivilegeGuard _sut = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var ctx = new TestExecutionContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new ApplicationDbContext(options, ctx, TenancyMode.SharedTables);
        await _db.Database.EnsureCreatedAsync();
        _sut = new PrivilegeGuard(new AuthorizationService(_db), _db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task EnsureCanGrant_Allows_When_Actor_Holds_It()
    {
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();
        await _sut.EnsureCanGrantAsync(_actor, Permission.SecretsWrite, Scope.Tenant);  // does not throw
    }

    [Fact]
    public async Task EnsureCanGrant_Throws_When_Actor_Lacks_It()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsureCanGrantAsync(_actor, Permission.SecretsWrite, Scope.Tenant));
    }

    [Fact]
    public async Task EnsureCanAssignRole_Requires_All_Role_Permissions()
    {
        var roleId = Guid.NewGuid();
        _db.Roles.Add(Role.CreateSystem(roleId, "TwoPerm", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        _db.RolePermissions.Add(RolePermission.Create(roleId, Permission.ProjectsRead));
        _db.RolePermissions.Add(RolePermission.Create(roleId, Permission.SecretsWrite));
        // actor holds only one of the two
        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.ProjectsRead, Scope.Tenant));
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.EnsureCanAssignRoleAsync(_actor, roleId, Scope.Tenant));

        _db.PermissionGrants.Add(PermissionGrant.Create(_tenant, _actor, Permission.SecretsWrite, Scope.Tenant));
        await _db.SaveChangesAsync();
        await _sut.EnsureCanAssignRoleAsync(_actor, roleId, Scope.Tenant);  // now holds both → ok
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
