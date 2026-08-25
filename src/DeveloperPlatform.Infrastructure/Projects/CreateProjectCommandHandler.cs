using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Projects.CreateProject;
using DeveloperPlatform.Domain.Projects;

namespace DeveloperPlatform.Infrastructure.Projects;

public sealed class CreateProjectCommandHandler(
    IProjectRepository repository,
    IExecutionContext executionContext)
    : ICommandHandler<CreateProjectCommand, CreateProjectResult>
{
    public async Task<CreateProjectResult> HandleAsync(
        CreateProjectCommand command, CancellationToken ct = default)
    {
        var project = Project.Create(executionContext.TenantId, command.Name, command.Description);
        await repository.AddAsync(project, ct);
        return new CreateProjectResult(project.Id);
    }
}
