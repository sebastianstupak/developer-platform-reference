using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RollbackSecret;
using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RollbackSecretCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RollbackSecretCommand, Unit>
{
    public async Task<Unit> HandleAsync(RollbackSecretCommand command, CancellationToken ct = default)
    {
        var secret = await repository.GetAsync(command.EnvironmentId, command.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{command.Name}' not found.");
        var target = await repository.GetVersionAsync(secret.Id, command.TargetVersion, ct)
            ?? throw new KeyNotFoundException($"Version {command.TargetVersion} of '{command.Name}' not found.");

        var plaintext = await crypto.DecryptAsync(ctx.TenantId, target.EncryptedValue, target.KeyId, ct);
        var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, plaintext, ct);   // fresh current key

        secret.SetNewVersion(payload, keyId);   // advances CurrentVersion
        await repository.AddVersionAsync(SecretVersion.Create(
            ctx.TenantId, secret.Id, secret.CurrentVersion, payload, keyId,
            ctx.PrincipalId, ctx.PrincipalType?.ToString(), ctx.UserId,
            rolledBackFrom: command.TargetVersion), ct);

        return Unit.Value;
    }
}
