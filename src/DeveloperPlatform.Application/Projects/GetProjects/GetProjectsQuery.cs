using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Projects.GetProjects;

[RequiresPermission(Permission.ProjectsRead)]
public record GetProjectsQuery : IQuery<IReadOnlyList<ProjectSummary>>;

public record ProjectSummary(Guid Id, string Name, string? Description, DateTime CreatedAt);
