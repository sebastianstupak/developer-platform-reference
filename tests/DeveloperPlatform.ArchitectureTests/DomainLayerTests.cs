// tests/DeveloperPlatform.ArchitectureTests/DomainLayerTests.cs
using NetArchTest.Rules;
using DeveloperPlatform.Domain.Abstractions;

namespace DeveloperPlatform.ArchitectureTests;

public class DomainLayerTests
{
    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(IEntity).Assembly;

    [Fact]
    public void Domain_Has_No_Outward_Dependencies()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "DeveloperPlatform.Application",
                "DeveloperPlatform.Infrastructure",
                "DeveloperPlatform.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void All_Concrete_Domain_Entities_Implement_ITenantScoped()
    {
        // Tenant itself is NOT tenant-scoped (it IS the tenant root)
        var result = Types.InAssembly(DomainAssembly)
            .That().AreNotAbstract()
            .And().ImplementInterface(typeof(IEntity))
            .And().DoNotHaveNameMatching("Tenant$")
            .And().DoNotHaveNameMatching("TenantEncryptionKey")
            .Should().ImplementInterface(typeof(ITenantScoped))
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
