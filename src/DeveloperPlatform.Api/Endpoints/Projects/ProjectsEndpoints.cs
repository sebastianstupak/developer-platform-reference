using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Projects.CreateProject;
using DeveloperPlatform.Application.Projects.DeleteProject;
using DeveloperPlatform.Application.Projects.GetProjects;
using DeveloperPlatform.Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Projects;

public static class ProjectsEndpoints
{
    public static IEndpointRouteBuilder MapProjects(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/projects")
            .WithTags("Projects")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(1)
            .RequireAuthorization();

        group.MapGet("/", async (
            IQueryDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var results = await dispatcher.SendAsync<GetProjectsQuery, IReadOnlyList<ProjectSummary>>(
                new GetProjectsQuery(), ct);

            return Results.Ok(results.Select(p => new ProjectResponse(
                p.Id, p.Name, p.Description, p.CreatedAt, p.EnvironmentCount, p.LastActivityAt)));
        })
        .WithName("GetProjects")
        .WithSummary("List projects")
        .Produces<IEnumerable<ProjectResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            [FromBody] CreateProjectRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync<CreateProjectCommand, CreateProjectResult>(
                new CreateProjectCommand(request.Name, request.Description), ct);

            return Results.Created(
                $"/api/v1/projects/{result.ProjectId}",
                new ProjectCreatedResponse(result.ProjectId));
        })
        .WithName("CreateProject")
        .WithSummary("Create project")
        .Produces<ProjectCreatedResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICommandDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync<DeleteProjectCommand, Unit>(
                new DeleteProjectCommand(id), ct);

            return Results.NoContent();
        })
        .WithName("DeleteProject")
        .WithSummary("Delete project")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public record CreateProjectRequest(string Name, string? Description = null);

    public record ProjectResponse(
        Guid Id, string Name, string? Description, DateTime CreatedAt,
        int EnvironmentCount, DateTime LastActivityAt);

    public record ProjectCreatedResponse(Guid ProjectId);
}
