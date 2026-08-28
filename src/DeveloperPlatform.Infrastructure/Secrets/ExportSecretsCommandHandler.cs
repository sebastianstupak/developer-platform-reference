using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.ExportSecrets;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class ExportSecretsCommandHandler(
    ISecretRepository repository, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<ExportSecretsCommand, ExportSecretsResult>
{
    public async Task<ExportSecretsResult> HandleAsync(ExportSecretsCommand command, CancellationToken ct = default)
    {
        var secrets = await repository.ListAsync(command.EnvironmentId, ct);
        var map = new Dictionary<string, string>(secrets.Count);
        foreach (var s in secrets)
        {
            map[s.Name] = await crypto.DecryptAsync(ctx.TenantId, s.EncryptedValue, s.KeyId, ct);
        }

        return new ExportSecretsResult(map);
    }
}
