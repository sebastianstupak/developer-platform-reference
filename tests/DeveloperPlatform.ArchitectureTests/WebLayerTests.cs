using NetArchTest.Rules;

namespace DeveloperPlatform.ArchitectureTests;

public class WebLayerTests
{
    [Fact]
    public void Web_Should_Not_Reference_Infrastructure_Directly()
    {
        var webAssembly = System.Reflection.Assembly.Load("DeveloperPlatform.Web");

        var result = Types.InAssembly(webAssembly)
            .ShouldNot().HaveDependencyOn("DeveloperPlatform.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Web_Should_Not_Reference_Domain_Directly()
    {
        var webAssembly = System.Reflection.Assembly.Load("DeveloperPlatform.Web");

        var result = Types.InAssembly(webAssembly)
            .ShouldNot().HaveDependencyOn("DeveloperPlatform.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
