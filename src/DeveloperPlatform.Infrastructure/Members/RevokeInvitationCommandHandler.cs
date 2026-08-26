using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Members.RevokeInvitation;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class RevokeInvitationCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokeInvitationCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeInvitationCommand command, CancellationToken ct = default)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.Id == command.InvitationId, ct)
            ?? throw new KeyNotFoundException($"Invitation {command.InvitationId} not found.");
        inv.Revoke();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
