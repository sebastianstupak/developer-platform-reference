using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecrets;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class ListSecretsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<ListSecretsQuery, IReadOnlyList<SecretSummary>>
{
    public async Task<IReadOnlyList<SecretSummary>> HandleAsync(ListSecretsQuery query, CancellationToken ct = default)
        => await db.Secrets.AsNoTracking()
            .Where(s => s.EnvironmentId == query.EnvironmentId)
            .OrderBy(s => s.Name)
            .Select(s => new SecretSummary(s.Name, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(ct);
}
