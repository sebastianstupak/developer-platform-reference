using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Infrastructure.Dispatching;
using NetArchTest.Rules;

namespace DeveloperPlatform.ArchitectureTests;

public class InfrastructureLayerTests
{
    private static readonly System.Reflection.Assembly InfraAssembly =
        typeof(CommandDispatcher).Assembly;

    [Fact]
    public void Infrastructure_Does_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(InfraAssembly)
            .ShouldNot().HaveDependencyOn("DeveloperPlatform.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void CommandHandlers_Should_End_With_CommandHandler()
    {
        var result = Types.InAssembly(InfraAssembly)
            .That().ImplementInterface(typeof(ICommandHandler<,>))
            .Should().HaveNameEndingWith("CommandHandler")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
