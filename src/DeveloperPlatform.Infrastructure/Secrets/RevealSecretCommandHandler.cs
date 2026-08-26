using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RevealSecret;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RevealSecretCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RevealSecretCommand, RevealSecretResult>
{
    public async Task<RevealSecretResult> HandleAsync(RevealSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        var value = await crypto.DecryptAsync(ctx.TenantId, secret.EncryptedValue, secret.KeyId, ct);
        return new RevealSecretResult(secret.Name, value);
    }
}
