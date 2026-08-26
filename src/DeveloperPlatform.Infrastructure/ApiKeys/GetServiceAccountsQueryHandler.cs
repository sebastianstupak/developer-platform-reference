using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.ServiceAccounts.GetServiceAccounts;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class GetServiceAccountsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetServiceAccountsQuery, IReadOnlyList<ServiceAccountSummary>>
{
    public async Task<IReadOnlyList<ServiceAccountSummary>> HandleAsync(
        GetServiceAccountsQuery query, CancellationToken ct = default)
        => await db.ServiceAccounts.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ServiceAccountSummary(s.PrincipalId, s.Name, s.Description, s.CreatedAt))
            .ToListAsync(ct);
}
