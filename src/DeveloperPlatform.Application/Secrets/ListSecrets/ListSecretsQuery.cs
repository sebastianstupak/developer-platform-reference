using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.ListSecrets;

[RequiresPermission(Permission.SecretsRead)]
public record ListSecretsQuery(Guid ProjectId, Guid EnvironmentId)
    : IQuery<IReadOnlyList<SecretSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record SecretSummary(string Name, DateTime CreatedAt, DateTime UpdatedAt);
