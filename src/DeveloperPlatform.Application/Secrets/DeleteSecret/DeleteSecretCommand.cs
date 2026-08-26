using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.DeleteSecret;

[RequiresPermission(Permission.SecretsWrite)]
public record DeleteSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}
