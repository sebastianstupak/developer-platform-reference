using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Domain.Authorization;

namespace DeveloperPlatform.Application.Projects.DeleteProject;

[RequiresPermission(Permission.ProjectsWrite)]
public record DeleteProjectCommand(Guid ProjectId) : ICommand<Unit>, IResourceScoped
{
    public Scope ResourceScope => Scope.Project(ProjectId);
}
