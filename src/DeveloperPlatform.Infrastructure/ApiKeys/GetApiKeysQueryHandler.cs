using DeveloperPlatform.Application.ApiKeys.GetApiKeys;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.ApiKeys;

public sealed class GetApiKeysQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetApiKeysQuery, IReadOnlyList<ApiKeySummary>>
{
    public async Task<IReadOnlyList<ApiKeySummary>> HandleAsync(GetApiKeysQuery query, CancellationToken ct = default)
    {
        return await db.ApiKeyCredentials.AsNoTracking()
            .Where(c => c.ServiceAccountId == query.ServiceAccountId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ApiKeySummary(
                c.Id, c.Name, c.KeyPrefix, c.ExpiresAt, c.IsRevoked, c.LastUsedAt, c.CreatedAt))
            .ToListAsync(ct);
    }
}
