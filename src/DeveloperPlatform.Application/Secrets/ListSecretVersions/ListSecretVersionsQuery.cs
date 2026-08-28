using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Secrets.ListSecretVersions;

[RequiresPermission(Permission.SecretsRead)]
public record ListSecretVersionsQuery(Guid ProjectId, Guid EnvironmentId, string Name)
    : IQuery<IReadOnlyList<SecretVersionSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Environment(EnvironmentId);
}

public record SecretVersionSummary(int VersionNumber, DateTime CreatedAt, string? Actor, bool IsCurrent, int? RolledBackFrom);
