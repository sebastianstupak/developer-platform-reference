using System.Security.Cryptography;
using System.Text;
using DeveloperPlatform.Application.ApiKeys.CreateApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Domain.ApiKeys;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class CreateApiKeyCommandHandler(
    IApiKeyRepository repository,
    IExecutionContext executionContext)
    : ICommandHandler<CreateApiKeyCommand, CreateApiKeyResult>
{
    public async Task<CreateApiKeyResult> HandleAsync(
        CreateApiKeyCommand command, CancellationToken ct = default)
    {
        // Generate a secure random key: "dpk_" + 32 random bytes as base64url
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextKey = "dpk_" + Convert.ToBase64String(rawBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // Store only a SHA-256 hash of the key (never the plaintext)
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey)));
        var keyPrefix = plaintextKey[..12]; // "dpk_" + 8 chars

        var apiKey = ApiKey.Create(
            tenantId: executionContext.TenantId,
            projectId: command.ProjectId,
            environmentId: command.EnvironmentId,
            name: command.Name,
            keyPrefix: keyPrefix,
            keyHash: keyHash,
            scopes: command.Scopes,
            expiresAt: command.ExpiresAt);

        await repository.AddAsync(apiKey, ct);

        return new CreateApiKeyResult(apiKey.Id, plaintextKey);
    }
}
