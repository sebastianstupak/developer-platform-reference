using DeveloperPlatform.Application.Members.GetMembers;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Members;

public sealed class GetMembersQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetMembersQuery, IReadOnlyList<MemberSummary>>
{
    public async Task<IReadOnlyList<MemberSummary>> HandleAsync(GetMembersQuery query, CancellationToken ct = default)
    {
        // Memberships are tenant-filtered; join the global Users table for identity.
        var rows = await db.Memberships.AsNoTracking()
            .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .Select(x => new MemberSummary(
                x.m.PrincipalId, x.u.Id, x.u.Email, x.u.DisplayName, x.m.Status.ToString()))
            .ToListAsync(ct);
        return rows;
    }
}
