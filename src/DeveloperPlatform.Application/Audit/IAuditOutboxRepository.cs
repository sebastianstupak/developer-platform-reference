using DeveloperPlatform.Domain.Audit;

namespace DeveloperPlatform.Application.Audit;

public interface IAuditOutboxRepository
{
    Task AddAsync(AuditOutboxEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditOutboxEntry>> GetPendingAsync(int batchSize, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid id, CancellationToken ct = default);
    Task MarkFailedAsync(Guid id, CancellationToken ct = default);
}
