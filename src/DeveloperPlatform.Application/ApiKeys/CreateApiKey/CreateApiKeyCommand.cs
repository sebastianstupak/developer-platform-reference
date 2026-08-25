using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.ApiKeys;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.CreateApiKey;

[RequiresPermission(Permission.ApiKeysManage)]
public record CreateApiKeyCommand(
    Guid ProjectId,
    Guid? EnvironmentId,
    [property: SensitiveData] string Name,
    ApiKeyScope Scopes,
    DateTime? ExpiresAt) : ICommand<CreateApiKeyResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}

public record CreateApiKeyResult(Guid ApiKeyId, string PlaintextKey);
