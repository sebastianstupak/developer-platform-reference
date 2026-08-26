using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Environments.RenameEnvironment;

[RequiresPermission(Permission.ProjectsWrite)]
public record RenameEnvironmentCommand(Guid ProjectId, Guid EnvironmentId, string Name)
    : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}
