using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Projects.CreateProject;

[RequiresPermission(Permission.ProjectsWrite)]
public record CreateProjectCommand(string Name, string? Description) : ICommand<CreateProjectResult>;

public record CreateProjectResult(Guid ProjectId);
