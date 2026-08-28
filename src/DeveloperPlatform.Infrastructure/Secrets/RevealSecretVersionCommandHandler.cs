using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RevealSecretVersion;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RevealSecretVersionCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RevealSecretVersionCommand, RevealSecretVersionResult>
{
    public async Task<RevealSecretVersionResult> HandleAsync(RevealSecretVersionCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        var version = await repository.GetVersionAsync(secret.Id, command.VersionNumber, ct)
            ?? throw new KeyNotFoundException($"Version {command.VersionNumber} of '{command.Name}' not found.");
        var value = await crypto.DecryptAsync(ctx.TenantId, version.EncryptedValue, version.KeyId, ct);
        return new RevealSecretVersionResult(secret.Name, version.VersionNumber, value);
    }
}
