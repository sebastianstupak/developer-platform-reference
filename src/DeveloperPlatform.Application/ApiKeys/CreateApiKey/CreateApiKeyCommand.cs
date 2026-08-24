using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.ApiKeys;

namespace DeveloperPlatform.Application.ApiKeys.CreateApiKey;

public record CreateApiKeyCommand(
    Guid ProjectId,
    Guid? EnvironmentId,
    [property: SensitiveData] string Name,
    ApiKeyScope Scopes,
    DateTime? ExpiresAt) : ICommand<CreateApiKeyResult>;

public record CreateApiKeyResult(Guid ApiKeyId, string PlaintextKey);
