using DeveloperPlatform.Application.ApiKeys.RevokeApiKey;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class RevokeApiKeyCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokeApiKeyCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeApiKeyCommand command, CancellationToken ct = default)
    {
        var credential = await db.ApiKeyCredentials.FirstOrDefaultAsync(c => c.Id == command.CredentialId, ct)
            ?? throw new KeyNotFoundException($"API key credential {command.CredentialId} not found.");
        credential.Revoke();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
