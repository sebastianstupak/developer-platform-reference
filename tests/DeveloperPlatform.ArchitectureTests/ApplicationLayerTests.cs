using NetArchTest.Rules;
using DeveloperPlatform.Application.Commands;

namespace DeveloperPlatform.ArchitectureTests;

public class ApplicationLayerTests
{
    private static readonly System.Reflection.Assembly AppAssembly =
        typeof(ICommandDispatcher).Assembly;

    [Fact]
    public void Application_Does_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(AppAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("DeveloperPlatform.Infrastructure", "DeveloperPlatform.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
