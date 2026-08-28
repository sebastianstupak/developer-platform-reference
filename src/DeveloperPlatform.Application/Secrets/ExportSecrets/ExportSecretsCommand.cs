using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.ExportSecrets;

[RequiresPermission(Permission.SecretsRead)]
public record ExportSecretsCommand(Guid ProjectId, Guid EnvironmentId)
    : ICommand<ExportSecretsResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record ExportSecretsResult(IReadOnlyDictionary<string, string> Secrets);
