using Asp.Versioning;
using Asp.Versioning.Builder;
using DeveloperPlatform.Application.ApiKeys.GetApiKeys;
using DeveloperPlatform.Application.ApiKeys.IssueApiKey;
using DeveloperPlatform.Application.ApiKeys.RevokeApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace DeveloperPlatform.Api.Endpoints.ApiKeys;

public static class ApiKeysEndpoints
{
    public static IEndpointRouteBuilder MapApiKeys(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v1/service-accounts/{serviceAccountId:guid}/keys")
            .WithTags("API Keys").WithApiVersionSet(versionSet).MapToApiVersion(1).RequireAuthorization();

        group.MapPost("/", async (
            Guid serviceAccountId, [FromBody] IssueApiKeyRequest request,
            ICommandDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync<IssueApiKeyCommand, IssueApiKeyResult>(
                new IssueApiKeyCommand(serviceAccountId, request.Name, request.ExpiresAt), ct);
            return Results.Created(
                $"/api/v1/service-accounts/{serviceAccountId}/keys/{result.CredentialId}",
                new IssueApiKeyResponse(result.CredentialId, result.PlaintextKey, result.KeyPrefix));
        })
        .WithName("IssueApiKey").WithSummary("Issue an API key")
        .WithDescription("The plaintext key is returned **once**. Only a SHA-256 hash is stored.")
        .Produces<IssueApiKeyResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", async (
            Guid serviceAccountId, IQueryDispatcher dispatcher, CancellationToken ct) =>
        {
            var keys = await dispatcher.SendAsync<GetApiKeysQuery, IReadOnlyList<ApiKeySummary>>(
                new GetApiKeysQuery(serviceAccountId), ct);
            return Results.Ok(keys);
        })
        .WithName("GetApiKeys").WithSummary("List a service account's API keys (metadata only)")
        .Produces<IReadOnlyList<ApiKeySummary>>(StatusCodes.Status200OK);

        group.MapPost("/{credentialId:guid}/revoke", async (
            Guid credentialId, ICommandDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync<RevokeApiKeyCommand, Unit>(new RevokeApiKeyCommand(credentialId), ct);
            return Results.NoContent();
        })
        .WithName("RevokeApiKey").WithSummary("Revoke an API key")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public record IssueApiKeyRequest(string Name, DateTime? ExpiresAt);
    public record IssueApiKeyResponse(Guid CredentialId, string PlaintextKey, string KeyPrefix,
        string Warning = "Store this key — it cannot be shown again.");
}
