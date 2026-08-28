using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Secrets.ListSecretVersions;
using DeveloperPlatform.Infrastructure.Common;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Secrets;

public sealed class ListSecretVersionsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<ListSecretVersionsQuery, IReadOnlyList<SecretVersionSummary>>
{
    public async Task<IReadOnlyList<SecretVersionSummary>> HandleAsync(
        ListSecretVersionsQuery query, CancellationToken ct = default)
    {
        var secret = await db.Secrets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EnvironmentId == query.EnvironmentId && s.Name == query.Name, ct)
            ?? throw new KeyNotFoundException($"Secret '{query.Name}' not found.");

        var rows = await db.SecretVersions.AsNoTracking()
            .Where(v => v.SecretId == secret.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.VersionNumber,
                v.CreatedAt,
                v.CreatedByPrincipalType,
                v.CreatedByUserId,
                v.CreatedByPrincipalId,
                v.RolledBackFrom
            })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.CreatedByUserId is not null).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var principalIds = rows.Where(r => r.CreatedByPrincipalId is not null).Select(r => r.CreatedByPrincipalId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var sas = await db.ServiceAccounts.AsNoTracking()
            .Where(s => principalIds.Contains(s.PrincipalId)).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct);

        return rows.Select(r => new SecretVersionSummary(
            r.VersionNumber, r.CreatedAt,
            ActorResolver.Resolve(r.CreatedByPrincipalType, r.CreatedByUserId, r.CreatedByPrincipalId, users, sas),
            r.VersionNumber == secret.CurrentVersion,
            r.RolledBackFrom)).ToList();
    }
}
