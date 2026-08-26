using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecrets;
using DeveloperPlatform.Application.Secrets.RevealSecret;
using DeveloperPlatform.Application.Secrets.SetSecret;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.Secrets;

public static class SecretsEndpoints
{
    public static IEndpointRouteBuilder MapSecrets(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}/environments/{environmentId:guid}/secrets")
            .WithTags("Secrets").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapPut("/{name}", async (Guid projectId, Guid environmentId, string name,
            [FromBody] SetSecretRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<SetSecretCommand, Unit>(new SetSecretCommand(projectId, environmentId, name, req.Value), ct);
            return Results.NoContent();
        }).WithName("SetSecret").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (Guid projectId, Guid environmentId, IQueryDispatcher d, CancellationToken ct) =>
        {
            var results = await d.SendAsync<ListSecretsQuery, IReadOnlyList<SecretSummary>>(
                new ListSecretsQuery(projectId, environmentId), ct);
            return Results.Ok(results.Select(s => new SecretResponse(s.Name, s.CreatedAt, s.UpdatedAt)));
        }).WithName("ListSecrets").Produces<IEnumerable<SecretResponse>>();

        group.MapPost("/{name}/reveal", async (Guid projectId, Guid environmentId, string name, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<RevealSecretCommand, RevealSecretResult>(
                new RevealSecretCommand(projectId, environmentId, name), ct);
            return Results.Ok(new RevealResponse(result.Name, result.Value));
        }).WithName("RevealSecret").Produces<RevealResponse>();

        return app;
    }

    public record SetSecretRequest(string Value);

    public record SecretResponse(string Name, DateTime CreatedAt, DateTime UpdatedAt);

    public record RevealResponse(string Name, string Value);
}
