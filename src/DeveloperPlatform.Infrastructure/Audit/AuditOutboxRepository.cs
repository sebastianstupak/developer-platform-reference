using DeveloperPlatform.Application.Audit;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class AuditOutboxRepository(ApplicationDbContext db) : IAuditOutboxRepository
{
    public async Task AddAsync(AuditOutboxEntry entry, CancellationToken ct = default)
        => await db.AuditOutboxEntries.AddAsync(entry, ct);

    // The outbox relay is a cross-tenant background process (no HTTP request → no ambient
    // tenant), so every read here bypasses the ITenantScoped global query filter, which would
    // otherwise match nothing (ctx.TenantId == Guid.Empty). Mirrors the API-key lookup idiom.
    public async Task<IReadOnlyList<AuditOutboxEntry>> GetPendingAsync(int batchSize, CancellationToken ct = default)
        => await db.AuditOutboxEntries
            .IgnoreQueryFilters()
            .Where(e => e.ProcessedAt == null && e.RetryCount < 5)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await db.AuditOutboxEntries.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new InvalidOperationException($"Outbox entry {id} not found.");
        entry.MarkProcessed();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await db.AuditOutboxEntries.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new InvalidOperationException($"Outbox entry {id} not found.");
        entry.MarkFailed();
        await db.SaveChangesAsync(ct);
    }
}
