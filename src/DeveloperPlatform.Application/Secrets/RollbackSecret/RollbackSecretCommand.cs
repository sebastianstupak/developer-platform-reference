using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RollbackSecret;

[RequiresPermission(Permission.SecretsWrite)]
public record RollbackSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, int TargetVersion)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}
