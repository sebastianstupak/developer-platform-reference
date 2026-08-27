using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Environments.CreateEnvironment;
using DeveloperPlatform.Application.Environments.DeleteEnvironment;
using DeveloperPlatform.Application.Environments.GetEnvironments;
using DeveloperPlatform.Application.Environments.RenameEnvironment;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Environments;

public static class EnvironmentsEndpoints
{
    public static IEndpointRouteBuilder MapEnvironments(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}/environments")
            .WithTags("Environments").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IQueryDispatcher d, CancellationToken ct) =>
        {
            var results = await d.SendAsync<GetEnvironmentsQuery, IReadOnlyList<EnvironmentSummary>>(
                new GetEnvironmentsQuery(projectId), ct);
            return Results.Ok(results.Select(e => new EnvironmentResponse(
                e.Id, e.Name, e.Type.ToString(), e.CreatedAt, e.SecretCount, e.LastUpdatedAt)));
        }).WithName("GetEnvironments").Produces<IEnumerable<EnvironmentResponse>>();

        group.MapPost("/", async (Guid projectId, [FromBody] CreateEnvironmentRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<CreateEnvironmentCommand, CreateEnvironmentResult>(
                new CreateEnvironmentCommand(projectId, req.Name, Enum.Parse<EnvironmentType>(req.Type)), ct);
            return Results.Created($"/api/v1/projects/{projectId}/environments/{result.EnvironmentId}",
                new EnvironmentCreatedResponse(result.EnvironmentId));
        }).WithName("CreateEnvironment").Produces<EnvironmentCreatedResponse>(StatusCodes.Status201Created);

        group.MapPut("/{environmentId:guid}", async (Guid projectId, Guid environmentId, [FromBody] RenameEnvironmentRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RenameEnvironmentCommand, Unit>(new RenameEnvironmentCommand(projectId, environmentId, req.Name), ct);
            return Results.NoContent();
        }).WithName("RenameEnvironment").Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/{environmentId:guid}", async (Guid projectId, Guid environmentId, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<DeleteEnvironmentCommand, Unit>(new DeleteEnvironmentCommand(projectId, environmentId), ct);
            return Results.NoContent();
        }).WithName("DeleteEnvironment").Produces(StatusCodes.Status204NoContent);

        return app;
    }

    public record CreateEnvironmentRequest(string Name, string Type);
    public record RenameEnvironmentRequest(string Name);
    public record EnvironmentResponse(
        Guid Id, string Name, string Type, DateTime CreatedAt,
        int SecretCount, DateTime LastUpdatedAt);
    public record EnvironmentCreatedResponse(Guid EnvironmentId);
}
