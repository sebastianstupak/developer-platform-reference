using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RevealSecret;

[RequiresPermission(Permission.SecretsRead)]
public record RevealSecretCommand(Guid ProjectId, Guid EnvironmentId, string Name)
    : ICommand<RevealSecretResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record RevealSecretResult(string Name, string Value);
