using DeveloperPlatform.Application.Environments.GetEnvironments;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Environments;

public sealed class GetEnvironmentsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetEnvironmentsQuery, IReadOnlyList<EnvironmentSummary>>
{
    public async Task<IReadOnlyList<EnvironmentSummary>> HandleAsync(GetEnvironmentsQuery query, CancellationToken ct = default)
        => await db.ProjectEnvironments.AsNoTracking()
            .Where(e => e.ProjectId == query.ProjectId)
            .OrderBy(e => e.Name)
            .Select(e => new EnvironmentSummary(
                e.Id, e.Name, e.Type, e.CreatedAt,
                db.Secrets.Count(s => s.EnvironmentId == e.Id),
                db.Secrets.Where(s => s.EnvironmentId == e.Id)
                    .Select(s => (DateTime?)s.UpdatedAt).Max() ?? e.CreatedAt))
            .ToListAsync(ct);
}
