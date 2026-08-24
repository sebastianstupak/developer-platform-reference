using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.ApiKeys.CreateApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.ApiKeys;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.ApiKeys;

public static class CreateApiKeyEndpoint
{
    public static IEndpointRouteBuilder MapCreateApiKey(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        app.MapPost("/api/v1/projects/{projectId:guid}/api-keys", async (
            Guid projectId,
            [FromBody] CreateApiKeyRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var command = new CreateApiKeyCommand(
                projectId,
                request.EnvironmentId,
                request.Name,
                request.Scopes,
                request.ExpiresAt);

            var result = await dispatcher.SendAsync<CreateApiKeyCommand, CreateApiKeyResult>(command, ct);

            return Results.Created(
                $"/api/v1/projects/{projectId}/api-keys/{result.ApiKeyId}",
                new CreateApiKeyResponse(result.ApiKeyId, result.PlaintextKey));
        })
        .WithName("CreateApiKey")
        .WithTags("API Keys")
        .WithSummary("Create API key")
        .WithDescription("""
            Creates a new API key scoped to a project or environment.
            The plaintext key is returned **once** — store it immediately.
            Only a SHA-256 hash is persisted; the plaintext cannot be recovered.
            """)
        .Produces<CreateApiKeyResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithApiVersionSet(versionSet)
        .MapToApiVersion(1);

        return app;
    }

    public record CreateApiKeyRequest(
        string Name,
        Guid? EnvironmentId,
        ApiKeyScope Scopes,
        DateTime? ExpiresAt);

    public record CreateApiKeyResponse(
        Guid ApiKeyId,
        string PlaintextKey,
        string Warning = "Store this key — it cannot be shown again.");
}
