using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Grants.AssignRole;
using DeveloperPlatform.Application.Grants.GrantPermission;
using DeveloperPlatform.Application.Grants.RevokePermissionGrant;
using DeveloperPlatform.Application.Grants.RevokeRoleAssignment;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Principals;

public static class PrincipalGrantsEndpoints
{
    public static IEndpointRouteBuilder MapPrincipalGrants(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/principals/{principalId:guid}")
            .WithTags("Access Management").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapPost("/role-assignments", async (
            Guid principalId, [FromBody] AssignRoleRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var r = await d.SendAsync<AssignRoleCommand, AssignRoleResult>(
                new AssignRoleCommand(principalId, req.RoleId, req.ScopeType, req.ScopeTargetId), ct);
            return Results.Created($"/api/v1/principals/{principalId}/role-assignments/{r.AssignmentId}", r);
        }).WithName("AssignRole").Produces<AssignRoleResult>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/role-assignments/{assignmentId:guid}", async (
            Guid assignmentId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RevokeRoleAssignmentCommand, Unit>(new RevokeRoleAssignmentCommand(assignmentId), ct);
            return Results.NoContent();
        }).WithName("RevokeRoleAssignment").Produces(StatusCodes.Status204NoContent);

        group.MapPost("/permission-grants", async (
            Guid principalId, [FromBody] GrantPermissionRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var r = await d.SendAsync<GrantPermissionCommand, GrantPermissionResult>(
                new GrantPermissionCommand(principalId, req.Permission, req.ScopeType, req.ScopeTargetId), ct);
            return Results.Created($"/api/v1/principals/{principalId}/permission-grants/{r.GrantId}", r);
        }).WithName("GrantPermission").Produces<GrantPermissionResult>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/permission-grants/{grantId:guid}", async (
            Guid grantId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RevokePermissionGrantCommand, Unit>(new RevokePermissionGrantCommand(grantId), ct);
            return Results.NoContent();
        }).WithName("RevokePermissionGrant").Produces(StatusCodes.Status204NoContent);

        return app;
    }

    public record AssignRoleRequest(Guid RoleId, ScopeType ScopeType, Guid? ScopeTargetId);
    public record GrantPermissionRequest(Permission Permission, ScopeType ScopeType, Guid? ScopeTargetId);
}
