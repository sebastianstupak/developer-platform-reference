using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Grants.GetRoles;
using DeveloperPlatform.Application.Members.GetMembers;
using DeveloperPlatform.Application.Queries;

namespace DeveloperPlatform.Api.Endpoints.Members;

public static class MembersEndpoints
{
    public static IEndpointRouteBuilder MapMembers(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapGet("/api/v1/roles", async (IQueryDispatcher d, CancellationToken ct) =>
            Results.Ok(await d.SendAsync<GetRolesQuery, IReadOnlyList<RoleSummary>>(new GetRolesQuery(), ct)))
            .WithName("GetRoles").WithTags("Access Management")
            .Produces<IReadOnlyList<RoleSummary>>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapGet("/api/v1/members", async (IQueryDispatcher d, CancellationToken ct) =>
            Results.Ok(await d.SendAsync<GetMembersQuery, IReadOnlyList<MemberSummary>>(new GetMembersQuery(), ct)))
            .WithName("GetMembers").WithTags("Access Management")
            .Produces<IReadOnlyList<MemberSummary>>(StatusCodes.Status200OK)
            .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        return app;
    }
}
