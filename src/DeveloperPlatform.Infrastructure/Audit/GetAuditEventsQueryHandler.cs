using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Infrastructure.Common;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class GetAuditEventsQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetAuditEventsQuery, PagedResult<AuditEventSummary>>
{
    public async Task<PagedResult<AuditEventSummary>> HandleAsync(
        GetAuditEventsQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize <= 0 ? 25 : query.PageSize, 1, 100);
        var f = query.Filter;

        var q = db.AuditEvents.AsNoTracking();
        if (f.From is { } from)
        {
            q = q.Where(e => e.OccurredAt >= from);
        }

        if (f.To is { } to)
        {
            q = q.Where(e => e.OccurredAt <= to);
        }

        if (f.PrincipalIds.Count > 0)
        {
            q = q.Where(e => e.PrincipalId != null && f.PrincipalIds.Contains(e.PrincipalId.Value));
        }

        if (f.CommandTypes.Count > 0)
        {
            q = q.Where(e => f.CommandTypes.Contains(e.CommandType));
        }

        if (f.Statuses.Count > 0)
        {
            q = q.Where(e => f.Statuses.Contains(e.Status));
        }

        if (f.CrossTenantOnly == true)
        {
            q = q.Where(e => e.IsCrossTenant);
        }

        if (f.ProjectId is { } projectId)
        {
            q = q.Where(e => e.ProjectId == projectId);
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * size).Take(size)
            .Select(e => new AuditEventRow(
                e.Id, e.OccurredAt, e.CommandType, e.Status,
                e.PrincipalId, e.PrincipalType, e.UserId, e.IpAddress, e.IsCrossTenant,
                e.ProjectId, e.EnvironmentId))
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var principalIds = rows.Where(r => r.PrincipalId is not null).Select(r => r.PrincipalId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var sas = await db.ServiceAccounts.AsNoTracking()
            .Where(s => principalIds.Contains(s.PrincipalId)).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct);

        var items = rows.Select(r => new AuditEventSummary(
            r.Id, r.OccurredAt, r.CommandType, r.Status,
            ActorResolver.Resolve(r.PrincipalType, r.UserId, r.PrincipalId, users, sas), r.PrincipalType, r.IpAddress, r.IsCrossTenant,
            r.ProjectId, r.EnvironmentId)).ToList();

        return new PagedResult<AuditEventSummary>(items, total, page, size);
    }

    private sealed record AuditEventRow(
        Guid Id, DateTime OccurredAt, string CommandType, AuditStatus Status,
        Guid? PrincipalId, string? PrincipalType, Guid? UserId, string IpAddress, bool IsCrossTenant,
        Guid? ProjectId, Guid? EnvironmentId);
}
