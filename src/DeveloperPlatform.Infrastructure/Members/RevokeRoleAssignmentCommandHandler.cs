using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Grants.RevokeRoleAssignment;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class RevokeRoleAssignmentCommandHandler(ApplicationDbContext db)
    : ICommandHandler<RevokeRoleAssignmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(RevokeRoleAssignmentCommand command, CancellationToken ct = default)
    {
        var assignment = await db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == command.AssignmentId, ct)
            ?? throw new KeyNotFoundException($"Role assignment {command.AssignmentId} not found.");
        db.RoleAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
