using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;
using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Application.Environments.CreateEnvironment;

[RequiresPermission(Permission.ProjectsWrite)]
public record CreateEnvironmentCommand(Guid ProjectId, string Name, EnvironmentType Type)
    : ICommand<CreateEnvironmentResult>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}

public record CreateEnvironmentResult(Guid EnvironmentId);
