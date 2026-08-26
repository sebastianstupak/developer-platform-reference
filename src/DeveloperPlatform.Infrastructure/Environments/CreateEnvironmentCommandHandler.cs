using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Environments.CreateEnvironment;
using DeveloperPlatform.Domain.Projects;
using DeveloperPlatform.Infrastructure.Projects;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class CreateEnvironmentCommandHandler(
    IProjectEnvironmentRepository repository, IExecutionContext ctx)
    : ICommandHandler<CreateEnvironmentCommand, CreateEnvironmentResult>
{
    public async Task<CreateEnvironmentResult> HandleAsync(CreateEnvironmentCommand command, CancellationToken ct = default)
    {
        var env = ProjectEnvironment.Create(ctx.TenantId, command.ProjectId, command.Name, command.Type);
        await repository.AddAsync(env, ct);
        return new CreateEnvironmentResult(env.Id);
    }
}
