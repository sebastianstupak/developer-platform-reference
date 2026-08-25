using DeveloperPlatform.Application.Commands;

namespace DeveloperPlatform.Application.Projects.DeleteProject;

public record DeleteProjectCommand(Guid ProjectId) : ICommand<Unit>;
