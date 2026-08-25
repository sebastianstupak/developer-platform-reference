using DeveloperPlatform.Api.Endpoints.Permissions;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Tests.Authorization;

public class PermissionsEndpointTests
{
    [Fact]
    public void BuildResponse_Returns_One_Row_Per_Catalog_Permission()
    {
        var response = PermissionsEndpoints.BuildResponse();
        Assert.Equal(PermissionCatalog.All.Count, response.Count);
    }

    [Fact]
    public void BuildResponse_Projects_SecretsWrite_With_Derived_Token()
    {
        var response = PermissionsEndpoints.BuildResponse();

        var row = Assert.Single(response, r => r.Token == "secrets:write");
        Assert.Equal("Secrets", row.Resource);
        Assert.Equal("Write", row.Action);
        Assert.False(string.IsNullOrWhiteSpace(row.Description));
    }
}
