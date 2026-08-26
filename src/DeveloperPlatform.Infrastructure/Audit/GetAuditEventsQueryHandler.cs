using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Common;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Audit;
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

        if (f.PrincipalId is { } pid)
        {
            q = q.Where(e => e.PrincipalId == pid);
        }

        if (!string.IsNullOrWhiteSpace(f.CommandType))
        {
            q = q.Where(e => e.CommandType == f.CommandType);
        }

        if (f.Status is { } st)
        {
            q = q.Where(e => e.Status == st);
        }

        if (f.CrossTenantOnly == true)
        {
            q = q.Where(e => e.IsCrossTenant);
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var userIds = rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var principalIds = rows.Where(r => r.PrincipalId is not null).Select(r => r.PrincipalId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var sas = await db.ServiceAccounts.AsNoTracking()
            .Where(s => principalIds.Contains(s.PrincipalId)).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct);

        var items = rows.Select(r => new AuditEventSummary(
            r.Id, r.OccurredAt, r.CommandType, r.Status,
            ResolveActor(r, users, sas), r.PrincipalType, r.IpAddress, r.IsCrossTenant,
            r.ProjectId, r.EnvironmentId)).ToList();

        return new PagedResult<AuditEventSummary>(items, total, page, size);
    }

    internal static string? ResolveActor(
        AuditEvent e, IReadOnlyDictionary<Guid, string> users, IReadOnlyDictionary<Guid, string> sas)
    {
        if (e.PrincipalType == "Member" && e.UserId is { } uid && users.TryGetValue(uid, out var email))
        {
            return email;
        }

        if (e.PrincipalType == "ServiceAccount" && e.PrincipalId is { } pid && sas.TryGetValue(pid, out var name))
        {
            return name;
        }

        return e.PrincipalId?.ToString();
    }
}
