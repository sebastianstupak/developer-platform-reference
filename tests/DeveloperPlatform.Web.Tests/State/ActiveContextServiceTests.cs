using DeveloperPlatform.Web.State;

namespace DeveloperPlatform.Web.Tests.State;

public class ActiveContextServiceTests
{
    [Fact]
    public void SetProject_Sets_Project_And_Raises_OnChange()
    {
        var sut = new ActiveContextService();
        var raised = 0;
        sut.OnChange += () => raised++;

        var p = new ActiveProjectRef(Guid.NewGuid(), "payments-api");
        sut.SetProject(p);

        Assert.Equal(p, sut.Project);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Changing_Project_Clears_Environment()
    {
        var sut = new ActiveContextService();
        sut.SetProject(new ActiveProjectRef(Guid.NewGuid(), "a"));
        sut.SetEnvironment(new ActiveEnvironmentRef(Guid.NewGuid(), "prod", "Production"));

        sut.SetProject(new ActiveProjectRef(Guid.NewGuid(), "b"));

        Assert.Null(sut.Environment);
    }

    [Fact]
    public void Setting_Same_Project_Keeps_Environment()
    {
        var sut = new ActiveContextService();
        var p = new ActiveProjectRef(Guid.NewGuid(), "a");
        sut.SetProject(p);
        var env = new ActiveEnvironmentRef(Guid.NewGuid(), "prod", "Production");
        sut.SetEnvironment(env);

        sut.SetProject(p);

        Assert.Equal(env, sut.Environment);
    }

    [Fact]
    public void Clear_Resets_Both_And_Raises()
    {
        var sut = new ActiveContextService();
        sut.SetProject(new ActiveProjectRef(Guid.NewGuid(), "a"));
        var raised = 0;
        sut.OnChange += () => raised++;

        sut.Clear();

        Assert.Null(sut.Project);
        Assert.Null(sut.Environment);
        Assert.Equal(1, raised);
    }
}
