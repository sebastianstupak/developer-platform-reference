using DeveloperPlatform.Application.Audit;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeveloperPlatform.Infrastructure.Audit;

public sealed class AuditOutboxRepository(ApplicationDbContext db) : IAuditOutboxRepository
{
    public async Task AddAsync(AuditOutboxEntry entry, CancellationToken ct = default)
        => await db.AuditOutboxEntries.AddAsync(entry, ct);

    public async Task<IReadOnlyList<AuditOutboxEntry>> GetPendingAsync(int batchSize, CancellationToken ct = default)
        => await db.AuditOutboxEntries
            .Where(e => e.ProcessedAt == null && e.RetryCount < 5)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await db.AuditOutboxEntries.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Outbox entry {id} not found.");
        entry.MarkProcessed();
    }

    public async Task MarkFailedAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await db.AuditOutboxEntries.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Outbox entry {id} not found.");
        entry.MarkFailed();
    }
}
