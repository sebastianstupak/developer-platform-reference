using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Environments.DeleteEnvironment;
using DeveloperPlatform.Infrastructure.Persistence;
using DeveloperPlatform.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class DeleteEnvironmentCommandHandler(
    IProjectEnvironmentRepository repository, ApplicationDbContext db)
    : ICommandHandler<DeleteEnvironmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteEnvironmentCommand command, CancellationToken ct = default)
    {
        var env = await repository.GetAsync(command.ProjectId, command.EnvironmentId, ct)
            ?? throw new KeyNotFoundException($"Environment {command.EnvironmentId} not found.");

        var secrets = await db.Secrets.Where(s => s.EnvironmentId == env.Id).ToListAsync(ct);
        db.Secrets.RemoveRange(secrets);
        repository.Delete(env);
        return Unit.Value;
    }
}
