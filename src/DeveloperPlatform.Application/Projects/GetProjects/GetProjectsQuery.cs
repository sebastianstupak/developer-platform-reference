using DeveloperPlatform.Application.Queries;

namespace DeveloperPlatform.Application.Projects.GetProjects;

public record GetProjectsQuery : IQuery<IReadOnlyList<ProjectSummary>>;

public record ProjectSummary(Guid Id, string Name, string? Description, DateTime CreatedAt);
