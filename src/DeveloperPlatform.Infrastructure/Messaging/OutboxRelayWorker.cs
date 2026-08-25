using DeveloperPlatform.Application.Audit;
using DeveloperPlatform.Domain.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeveloperPlatform.Infrastructure.Messaging;

public sealed class OutboxRelayWorker(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "OutboxRelayWorker failed during batch processing.");
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditOutboxRepository>();

        var entries = await repo.GetPendingAsync(BatchSize, ct);
        if (entries.Count == 0)
        {
            return;
        }

        logger.LogInformation("Relaying {Count} outbox entries.", entries.Count);

        foreach (var entry in entries)
        {
            try
            {
                var message = ToMessage(entry);
                await publisher.PublishAsync(message, ct);
                await repo.MarkProcessedAsync(entry.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to relay outbox entry {Id}.", entry.Id);
                await repo.MarkFailedAsync(entry.Id, ct);
            }
        }
    }

    private static AuditMessage ToMessage(AuditOutboxEntry entry) =>
        new(entry.Id, entry.TenantId, entry.CommandType, entry.Status.ToString(),
            entry.PrincipalId, entry.PrincipalType, entry.UserId, entry.ProjectId, entry.EnvironmentId,
            entry.IpAddress, entry.IsCrossTenant, entry.CrossTenantReason,
            entry.EncryptedPayload, entry.KeyId, entry.CreatedAt);
}
