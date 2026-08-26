using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Environments.RenameEnvironment;
using DeveloperPlatform.Infrastructure.Projects;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class RenameEnvironmentCommandHandler(IProjectEnvironmentRepository repository)
    : ICommandHandler<RenameEnvironmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(RenameEnvironmentCommand command, CancellationToken ct = default)
    {
        var env = await repository.GetAsync(command.ProjectId, command.EnvironmentId, ct)
            ?? throw new KeyNotFoundException($"Environment {command.EnvironmentId} not found.");
        env.Rename(command.Name);
        return Unit.Value;
    }
}
