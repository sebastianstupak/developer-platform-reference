using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Application.Environments.GetEnvironments;

[RequiresPermission(Permission.ProjectsRead)]
public record GetEnvironmentsQuery(Guid ProjectId) : IQuery<IReadOnlyList<EnvironmentSummary>>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}

public record EnvironmentSummary(
    Guid Id, string Name, EnvironmentType Type, DateTime CreatedAt,
    int SecretCount, DateTime LastUpdatedAt);
