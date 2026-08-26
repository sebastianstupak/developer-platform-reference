using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.SetSecret;

[RequiresPermission(Permission.SecretsWrite)]
public record SetSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name, [property: SensitiveData] string Value)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}
