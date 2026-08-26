using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RotateTenantKey;

[RequiresPermission(Permission.SecretsWrite)]
public record RotateTenantKeyCommand : ICommand<RotateTenantKeyResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Tenant;
}

public record RotateTenantKeyResult(int SecretsReEncrypted);
