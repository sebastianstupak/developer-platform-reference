using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Projects.DeleteProject;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Projects;

public sealed class DeleteProjectCommandHandler(
    IProjectRepository repository,
    ApplicationDbContext db)
    : ICommandHandler<DeleteProjectCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        DeleteProjectCommand command, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == command.ProjectId, ct)
            ?? throw new KeyNotFoundException($"Project {command.ProjectId} not found.");

        repository.Delete(project);
        return Unit.Value;
    }
}
