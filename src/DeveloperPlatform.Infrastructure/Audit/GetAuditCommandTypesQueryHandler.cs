using DeveloperPlatform.Application.Audit.GetAuditCommandTypes;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class GetAuditCommandTypesQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetAuditCommandTypesQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> HandleAsync(GetAuditCommandTypesQuery query, CancellationToken ct = default)
        => await db.AuditEvents.AsNoTracking()
            .Select(e => e.CommandType).Distinct().OrderBy(c => c).ToListAsync(ct);
}
