using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.RevealSecretVersion;

[RequiresPermission(Permission.SecretsRead)]
public record RevealSecretVersionCommand(Guid ProjectId, Guid EnvironmentId, string Name, int VersionNumber)
    : ICommand<RevealSecretVersionResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record RevealSecretVersionResult(string Name, int VersionNumber, [property: SensitiveData] string Value);
