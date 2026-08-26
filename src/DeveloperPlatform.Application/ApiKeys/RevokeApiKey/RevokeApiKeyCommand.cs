using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.ApiKeys.RevokeApiKey;

[RequiresPermission(Permission.ApiKeysManage)]
public record RevokeApiKeyCommand(Guid CredentialId) : ICommand;
