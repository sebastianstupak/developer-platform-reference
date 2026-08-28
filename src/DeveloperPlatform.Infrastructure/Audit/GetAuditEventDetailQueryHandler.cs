using DeveloperPlatform.Application.Audit.GetAuditEventDetail;
using DeveloperPlatform.Application.Audit.GetAuditEvents;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Infrastructure.Common;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class GetAuditEventDetailQueryHandler(
    ApplicationDbContext db, ITenantCryptoService crypto, IExecutionContext ctx)
    : IQueryHandler<GetAuditEventDetailQuery, AuditEventDetail>
{
    public async Task<AuditEventDetail> HandleAsync(GetAuditEventDetailQuery query, CancellationToken ct = default)
    {
        var e = await db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct)
            ?? throw new KeyNotFoundException($"Audit event {query.Id} not found.");

        var users = e.UserId is { } uid
            ? await db.Users.AsNoTracking().Where(u => u.Id == uid).ToDictionaryAsync(u => u.Id, u => u.Email, ct)
            : new Dictionary<Guid, string>();
        var sas = e.PrincipalId is { } pid
            ? await db.ServiceAccounts.AsNoTracking().Where(s => s.PrincipalId == pid).ToDictionaryAsync(s => s.PrincipalId, s => s.Name, ct)
            : new Dictionary<Guid, string>();

        var summary = new AuditEventSummary(
            e.Id, e.OccurredAt, e.CommandType, e.Status,
            ActorResolver.Resolve(e.PrincipalType, e.UserId, e.PrincipalId, users, sas), e.PrincipalType, e.IpAddress,
            e.IsCrossTenant, e.ProjectId, e.EnvironmentId);

        string payloadJson = "";
        var available = false;
        try
        {
            payloadJson = await crypto.DecryptAsync(ctx.TenantId, e.EncryptedPayload, e.KeyId, ct);
            available = true;
        }
        catch (InvalidOperationException)
        {
            // Key missing/shredded (e.g. rotated away or tenant crypto-shredded) — payload unrecoverable.
        }

        return new AuditEventDetail(summary, e.CrossTenantReason, payloadJson, available);
    }
}
