using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PermissionCatalogTests
{
    [Fact]
    public void All_Has_One_Descriptor_Per_Enum_Value()
    {
        var enumCount = Enum.GetValues<Permission>().Length;
        Assert.Equal(enumCount, PermissionCatalog.All.Count);
    }

    [Fact]
    public void Every_Descriptor_Has_NonEmpty_Token_And_Description()
    {
        Assert.All(PermissionCatalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Token));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
        });
    }

    [Fact]
    public void Token_Is_Derived_As_Resource_Colon_Action_Lowercased()
    {
        Assert.Equal("secrets:write", PermissionCatalog.ToToken(Permission.SecretsWrite));
        Assert.Equal("projects:read", PermissionCatalog.ToToken(Permission.ProjectsRead));
        Assert.Equal("api-keys:manage", PermissionCatalog.ToToken(Permission.ApiKeysManage));
        Assert.Equal("service-accounts:manage", PermissionCatalog.ToToken(Permission.ServiceAccountsManage));
    }

    [Fact]
    public void Tokens_Are_Unique()
    {
        var tokens = PermissionCatalog.All.Select(d => d.Token).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void ToToken_FromToken_RoundTrips_For_All_Permissions()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            var token = PermissionCatalog.ToToken(permission);
            Assert.Equal(permission, PermissionCatalog.FromToken(token));
        }
    }

    [Fact]
    public void FromToken_Throws_For_Unknown_Token()
    {
        Assert.Throws<ArgumentException>(() => PermissionCatalog.FromToken("nope:nope"));
    }
}
