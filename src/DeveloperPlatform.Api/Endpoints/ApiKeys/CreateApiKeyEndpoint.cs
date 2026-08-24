using DeveloperPlatform.Application.ApiKeys.CreateApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.ApiKeys;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.ApiKeys;

public static class CreateApiKeyEndpoint
{
    public static IEndpointRouteBuilder MapCreateApiKey(this IEndpointRouteBuilder app)
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
                new { result.ApiKeyId, result.PlaintextKey, Warning = "Store this key — it cannot be shown again." });
        })
        .WithName("CreateApiKey")
        .WithTags("ApiKeys");

        return app;
    }

    public record CreateApiKeyRequest(
        string Name,
        Guid? EnvironmentId,
        ApiKeyScope Scopes,
        DateTime? ExpiresAt);
}
