using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Secrets.RotateTenantKey;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class RotateTenantKeyCommandHandler(
    ApplicationDbContext db, ITenantCryptoService crypto, IExecutionContext ctx)
    : ICommandHandler<RotateTenantKeyCommand, RotateTenantKeyResult>
{
    public async Task<RotateTenantKeyResult> HandleAsync(RotateTenantKeyCommand command, CancellationToken ct = default)
    {
        // Add a new key and flush so GetActiveKeyAsync (a DB query) selects it as the newest.
        await crypto.CreateKeyAsync(ctx.TenantId, ct);
        await db.SaveChangesAsync(ct);

        var secrets = await db.Secrets.ToListAsync(ct);   // tenant filter already applied
        foreach (var secret in secrets)
        {
            var plaintext = await crypto.DecryptAsync(ctx.TenantId, secret.EncryptedValue, secret.KeyId, ct);
            var (payload, keyId) = await crypto.EncryptAsync(ctx.TenantId, plaintext, ct);
            secret.UpdateValue(payload, keyId);
        }

        return new RotateTenantKeyResult(secrets.Count);
    }
}
