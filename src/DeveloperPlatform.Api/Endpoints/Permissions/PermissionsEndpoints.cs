using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Api.Endpoints.Permissions;

public static class PermissionsEndpoints
{
    public static IEndpointRouteBuilder MapPermissions(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapGet("/api/v1/permissions", () => Results.Ok(BuildResponse()))
            .WithName("GetPermissions")
            .WithTags("Permissions")
            .WithSummary("List the permission catalog")
            .WithDescription("Returns every permission the platform enforces, as stable resource:action tokens.")
            .Produces<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(1)
            .RequireAuthorization();

        return app;
    }

    // Pure projection — unit-tested without booting the app.
    public static IReadOnlyList<PermissionResponse> BuildResponse() =>
        PermissionCatalog.All
            .Select(d => new PermissionResponse(
                d.Token,
                d.Resource.ToString(),
                d.Action.ToString(),
                d.Description))
            .ToList();

    public record PermissionResponse(string Token, string Resource, string Action, string Description);
}
