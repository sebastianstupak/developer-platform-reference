using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class ScopeTests
{
    private static readonly Guid Proj = Guid.NewGuid();
    private static readonly Guid Env = Guid.NewGuid();

    [Fact]
    public void Tenant_Scope_Has_No_Target()
    {
        var s = Scope.Tenant;
        Assert.Equal(ScopeType.Tenant, s.Type);
        Assert.Null(s.TargetId);
    }

    [Fact]
    public void Project_And_Environment_Require_A_Target()
    {
        Assert.Equal(Proj, Scope.Project(Proj).TargetId);
        Assert.Throws<ArgumentException>(() => Scope.Create(ScopeType.Project, null));
        Assert.Throws<ArgumentException>(() => Scope.Create(ScopeType.Environment, null));
    }

    [Fact]
    public void Tenant_Scope_Rejects_A_Target()
    {
        Assert.Throws<ArgumentException>(() => Scope.Create(ScopeType.Tenant, Proj));
    }

    [Fact]
    public void Tenant_Encompasses_Everything()
    {
        Assert.True(Scope.Tenant.Encompasses(Scope.Tenant));
        Assert.True(Scope.Tenant.Encompasses(Scope.Project(Proj)));
        Assert.True(Scope.Tenant.Encompasses(Scope.Environment(Env)));
    }

    [Fact]
    public void Project_Encompasses_Only_Itself()
    {
        var p = Scope.Project(Proj);
        Assert.True(p.Encompasses(Scope.Project(Proj)));
        Assert.False(p.Encompasses(Scope.Tenant));
        Assert.False(p.Encompasses(Scope.Project(Guid.NewGuid())));
        // Environment-under-a-project cascade is resolved by the authorization service (Slice 3),
        // which knows an environment's parent project; Scope alone treats them as distinct targets.
        Assert.False(p.Encompasses(Scope.Environment(Env)));
    }

    [Fact]
    public void Environment_Encompasses_Only_Itself()
    {
        var e = Scope.Environment(Env);
        Assert.True(e.Encompasses(Scope.Environment(Env)));
        Assert.False(e.Encompasses(Scope.Environment(Guid.NewGuid())));
    }
}
