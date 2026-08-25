using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Identity;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PrincipalModelTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void User_Create_Sets_Fields_And_Requires_Subject()
    {
        var u = User.Create("kc-sub-123", "dev@example.com", "Dev User");
        Assert.NotEqual(Guid.Empty, u.Id);
        Assert.Equal("kc-sub-123", u.KeycloakSubject);
        Assert.Equal("dev@example.com", u.Email);
        Assert.Equal("Dev User", u.DisplayName);
        Assert.Throws<ArgumentException>(() => User.Create("  ", "e@x.com", "n"));
    }

    [Fact]
    public void Principal_CreateMember_Is_Member_Type()
    {
        var p = Principal.CreateMember(Tenant, "Dev User");
        Assert.Equal(Tenant, p.TenantId);
        Assert.Equal(PrincipalType.Member, p.Type);
        Assert.Equal("Dev User", p.DisplayName);
    }

    [Fact]
    public void Principal_CreateServiceAccount_Is_ServiceAccount_Type()
    {
        var p = Principal.CreateServiceAccount(Tenant, "ci-deployer");
        Assert.Equal(PrincipalType.ServiceAccount, p.Type);
    }

    [Fact]
    public void Membership_Create_Links_Principal_And_User()
    {
        var principalId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var m = Membership.Create(Tenant, principalId, userId, MembershipStatus.Invited);
        Assert.Equal(principalId, m.PrincipalId);
        Assert.Equal(userId, m.UserId);
        Assert.Equal(MembershipStatus.Invited, m.Status);

        m.Activate();
        Assert.Equal(MembershipStatus.Active, m.Status);
        m.Suspend();
        Assert.Equal(MembershipStatus.Suspended, m.Status);
    }

    [Fact]
    public void ServiceAccount_Create_Requires_Name()
    {
        var principalId = Guid.NewGuid();
        var sa = ServiceAccount.Create(Tenant, principalId, "ci-deployer", "CI robot");
        Assert.Equal(principalId, sa.PrincipalId);
        Assert.Equal("ci-deployer", sa.Name);
        Assert.Throws<ArgumentException>(() => ServiceAccount.Create(Tenant, principalId, " ", null));
    }
}
