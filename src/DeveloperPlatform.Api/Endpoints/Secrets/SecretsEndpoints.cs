using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.DeleteSecret;
using DeveloperPlatform.Application.Secrets.ListSecrets;
using DeveloperPlatform.Application.Secrets.ListSecretVersions;
using DeveloperPlatform.Application.Secrets.RevealSecret;
using DeveloperPlatform.Application.Secrets.RevealSecretVersion;
using DeveloperPlatform.Application.Secrets.RollbackSecret;
using DeveloperPlatform.Application.Secrets.RotateTenantKey;
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

        group.MapDelete("/{name}", async (Guid projectId, Guid environmentId, string name, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<DeleteSecretCommand, Unit>(new DeleteSecretCommand(projectId, environmentId, name), ct);
            return Results.NoContent();
        }).WithName("DeleteSecret").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{name}/versions", async (Guid projectId, Guid environmentId, string name, IQueryDispatcher d, CancellationToken ct) =>
        {
            var results = await d.SendAsync<ListSecretVersionsQuery, IReadOnlyList<SecretVersionSummary>>(
                new ListSecretVersionsQuery(projectId, environmentId, name), ct);
            return Results.Ok(results.Select(v => new SecretVersionResponse(v.VersionNumber, v.CreatedAt, v.Actor, v.IsCurrent, v.RolledBackFrom)));
        }).WithName("ListSecretVersions").Produces<IEnumerable<SecretVersionResponse>>();

        group.MapPost("/{name}/versions/{version:int}/reveal", async (Guid projectId, Guid environmentId, string name, int version, ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<RevealSecretVersionCommand, RevealSecretVersionResult>(
                new RevealSecretVersionCommand(projectId, environmentId, name, version), ct);
            return Results.Ok(new RevealVersionResponse(result.Name, result.VersionNumber, result.Value));
        }).WithName("RevealSecretVersion").Produces<RevealVersionResponse>();

        group.MapPost("/{name}/rollback", async (Guid projectId, Guid environmentId, string name,
            [FromBody] RollbackSecretRequest req, ICommandDispatcher d, CancellationToken ct) =>
        {
            await d.SendAsync<RollbackSecretCommand, Unit>(new RollbackSecretCommand(projectId, environmentId, name, req.Version), ct);
            return Results.NoContent();
        }).WithName("RollbackSecret").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        var admin = app.MapGroup("/api/v1/secrets")
            .WithTags("Secrets").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();
        admin.MapPost("/rotate-key", async (ICommandDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync<RotateTenantKeyCommand, RotateTenantKeyResult>(new RotateTenantKeyCommand(), ct);
            return Results.Ok(new RotateKeyResponse(result.SecretsReEncrypted));
        }).WithName("RotateTenantKey").Produces<RotateKeyResponse>();

        return app;
    }

    public record SetSecretRequest(string Value);

    public record SecretResponse(string Name, DateTime CreatedAt, DateTime UpdatedAt);

    public record RevealResponse(string Name, string Value);

    public record RotateKeyResponse(int SecretsReEncrypted);

    public record SecretVersionResponse(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom);

    public record RevealVersionResponse(string Name, int VersionNumber, string Value);

    public record RollbackSecretRequest(int Version);
}
