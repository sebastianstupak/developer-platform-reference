using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Secrets.DeleteSecret;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class DeleteSecretCommandHandler(ISecretRepository repository)
    : ICommandHandler<DeleteSecretCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        repository.Delete(secret);
        return Unit.Value;
    }
}
