using DeveloperPlatform.Application.Members.GetInvitations;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GetInvitationsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetInvitationsQuery, IReadOnlyList<InvitationSummary>>
{
    public async Task<IReadOnlyList<InvitationSummary>> HandleAsync(GetInvitationsQuery query, CancellationToken ct = default)
        => await db.Invitations.AsNoTracking().OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationSummary(i.Id, i.Email, i.RoleId, i.Status.ToString(), i.ExpiresAt))
            .ToListAsync(ct);
}
