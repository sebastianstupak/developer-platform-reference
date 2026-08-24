using NetArchTest.Rules;

namespace DeveloperPlatform.ArchitectureTests;

public class ApiLayerTests
{
    private const string ApiNamespace = "DeveloperPlatform.Api";

    [Fact]
    public void Api_Controllers_Should_Be_Sealed()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespace(ApiNamespace)
            .And().HaveNameEndingWith("Controller")
            .Should().BeSealed()
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Api_Should_Not_Reference_Test_Assemblies()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespace(ApiNamespace)
            .ShouldNot().HaveDependencyOn("xunit")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    private static string FormatFailures(TestResult result) =>
        string.Join(", ", result.FailingTypeNames ?? []);
}
