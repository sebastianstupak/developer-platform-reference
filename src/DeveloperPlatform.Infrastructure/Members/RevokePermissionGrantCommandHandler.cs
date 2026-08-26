using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Grants.RevokePermissionGrant;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class RevokePermissionGrantCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokePermissionGrantCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokePermissionGrantCommand command, CancellationToken ct = default)
    {
        var grant = await db.PermissionGrants.FirstOrDefaultAsync(g => g.Id == command.GrantId, ct)
            ?? throw new KeyNotFoundException($"Permission grant {command.GrantId} not found.");
        db.PermissionGrants.Remove(grant);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
