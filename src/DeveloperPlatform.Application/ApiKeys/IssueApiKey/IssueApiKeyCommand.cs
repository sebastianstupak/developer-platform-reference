using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.IssueApiKey;

[RequiresPermission(Permission.ApiKeysManage)]
public record IssueApiKeyCommand(Guid ServiceAccountId, string Name, DateTime? ExpiresAt)
    : ICommand<IssueApiKeyResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}

public record IssueApiKeyResult(Guid CredentialId, string PlaintextKey, string KeyPrefix);
