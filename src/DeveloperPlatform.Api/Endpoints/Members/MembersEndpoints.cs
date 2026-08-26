using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
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

        app.MapPost("/api/v1/invitations", async (
            [Microsoft.AspNetCore.Mvc.FromBody] InviteRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var r = await d.SendAsync<DeveloperPlatform.Application.Members.InviteMember.InviteMemberCommand,
                DeveloperPlatform.Application.Members.InviteMember.InviteMemberResult>(
                new(req.Email, req.RoleId, req.ScopeType, req.ScopeTargetId), ct);
            return Results.Created($"/api/v1/invitations/{r.InvitationId}", r);
        }).WithName("InviteMember").WithTags("Access Management")
          .Produces<DeveloperPlatform.Application.Members.InviteMember.InviteMemberResult>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapGet("/api/v1/invitations", async (IQueryDispatcher d, CancellationToken ct) =>
            Results.Ok(await d.SendAsync<DeveloperPlatform.Application.Members.GetInvitations.GetInvitationsQuery,
                IReadOnlyList<DeveloperPlatform.Application.Members.GetInvitations.InvitationSummary>>(new(), ct)))
            .WithName("GetInvitations").WithTags("Access Management")
            .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        app.MapPost("/api/v1/invitations/{invitationId:guid}/revoke", async (
            Guid invitationId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<DeveloperPlatform.Application.Members.RevokeInvitation.RevokeInvitationCommand,
                DeveloperPlatform.Application.Commands.Unit>(new(invitationId), ct);
            return Results.NoContent();
        }).WithName("RevokeInvitation").WithTags("Access Management")
          .WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        return app;
    }

    public record InviteRequest(string Email, Guid RoleId, DeveloperPlatform.Domain.Authorization.ScopeType ScopeType, Guid? ScopeTargetId);
}
