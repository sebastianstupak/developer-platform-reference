using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class GrantModelTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Principal = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid Proj = Guid.NewGuid();
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Role_CreateSystem_Is_System()
    {
        var r = Role.CreateSystem(RoleId, "Owner", Seed);
        Assert.Equal(RoleId, r.Id);
        Assert.Equal("Owner", r.Name);
        Assert.True(r.IsSystem);
        Assert.Equal(Seed, r.CreatedAt);
    }

    [Fact]
    public void RolePermission_Links_Role_And_Permission()
    {
        var rp = RolePermission.Create(RoleId, Permission.SecretsWrite);
        Assert.Equal(RoleId, rp.RoleId);
        Assert.Equal(Permission.SecretsWrite, rp.Permission);
    }

    [Fact]
    public void RoleAssignment_Stores_Scope_As_Columns()
    {
        var a = RoleAssignment.Create(Tenant, Principal, RoleId, Scope.Project(Proj));
        Assert.Equal(Principal, a.PrincipalId);
        Assert.Equal(RoleId, a.RoleId);
        Assert.Equal(ScopeType.Project, a.ScopeType);
        Assert.Equal(Proj, a.ScopeTargetId);
        Assert.Equal(Scope.Project(Proj), a.Scope);
    }

    [Fact]
    public void PermissionGrant_TenantScope_Has_Null_Target()
    {
        var g = PermissionGrant.Create(Tenant, Principal, Permission.AuditRead, Scope.Tenant);
        Assert.Equal(Permission.AuditRead, g.Permission);
        Assert.Equal(ScopeType.Tenant, g.ScopeType);
        Assert.Null(g.ScopeTargetId);
        Assert.Equal(Scope.Tenant, g.Scope);
    }

    [Fact]
    public void Invitation_Lifecycle()
    {
        var inv = Invitation.Create(Tenant, "new@example.com", RoleId, Scope.Tenant, "tok-123", Seed.AddDays(7));
        Assert.Equal(InvitationStatus.Pending, inv.Status);
        Assert.Equal("new@example.com", inv.Email);

        inv.Accept();
        Assert.Equal(InvitationStatus.Accepted, inv.Status);

        var inv2 = Invitation.Create(Tenant, "x@example.com", RoleId, Scope.Tenant, "tok-9", Seed.AddDays(7));
        inv2.Revoke();
        Assert.Equal(InvitationStatus.Revoked, inv2.Status);
    }

    [Fact]
    public void Invitation_Requires_Email_And_Token()
    {
        Assert.Throws<ArgumentException>(() => Invitation.Create(Tenant, " ", RoleId, Scope.Tenant, "t", Seed));
        Assert.Throws<ArgumentException>(() => Invitation.Create(Tenant, "e@x.com", RoleId, Scope.Tenant, " ", Seed));
    }
}
