using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Environments.DeleteEnvironment;

[RequiresPermission(Permission.ProjectsWrite)]
public record DeleteEnvironmentCommand(Guid ProjectId, Guid EnvironmentId)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}
