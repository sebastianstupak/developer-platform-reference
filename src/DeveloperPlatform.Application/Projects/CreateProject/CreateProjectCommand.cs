using DeveloperPlatform.Application.Commands;

namespace DeveloperPlatform.Application.Projects.CreateProject;

public record CreateProjectCommand(string Name, string? Description) : ICommand<CreateProjectResult>;

public record CreateProjectResult(Guid ProjectId);
