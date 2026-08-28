using System.Text;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.SetSecret;
using DeveloperPlatform.Domain.Secrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class SetSecretCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<SetSecretCommand, Unit>
{
    private const int MaxValueBytes = 64 * 1024;

    public async Task<Unit> HandleAsync(SetSecretCommand command, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(command.Value) > MaxValueBytes)
        {
            throw new ArgumentException($"Secret value exceeds {MaxValueBytes} bytes.");
        }

        var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, command.Value, ct);
        var existing = await repository.GetAsync(command.EnvironmentId, command.Name, ct);
        Secret secret;
        if (existing is null)
        {
            secret = Secret.Create(ctx.TenantId, command.ProjectId, command.EnvironmentId, command.Name, payload, keyId);
            await repository.AddAsync(secret, ct);
        }
        else
        {
            secret = existing;
            secret.SetNewVersion(payload, keyId);
        }

        await repository.AddVersionAsync(SecretVersion.Create(
            ctx.TenantId, secret.Id, secret.CurrentVersion, payload, keyId,
            ctx.PrincipalId, ctx.PrincipalType?.ToString(), ctx.UserId), ct);

        return Unit.Value;
    }
}
