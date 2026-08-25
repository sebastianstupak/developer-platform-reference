using System.Text;
using System.Text.Json;
using DeveloperPlatform.Domain.Audit;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DeveloperPlatform.Infrastructure.Messaging;

public sealed class AuditConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditConsumer> logger,
    string hostName) : BackgroundService
{
    private const string ExchangeName = "developer-platform.audit";
    private const string QueueName = "developer-platform.audit.events";
    private IConnection? _connection;
    private IChannel? _channel;

    public override async Task StartAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory { HostName = hostName };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(QueueName, ExchangeName, "audit.#", cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: ct);

        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_channel is null)
        {
            return;
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                var message = JsonSerializer.Deserialize<AuditMessage>(json)!;

                await PersistAsync(message, ct);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process audit message.");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            }
        };

        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: ct);

        // Keep alive until cancelled
        await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }, CancellationToken.None);
    }

    private async Task PersistAsync(AuditMessage message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var status = Enum.Parse<AuditStatus>(message.Status);
        var ev = AuditEvent.Create(
            message.TenantId, message.OccurredAt, message.CommandType, status,
            message.PrincipalId, message.PrincipalType, message.UserId, message.ProjectId, message.EnvironmentId,
            message.IpAddress, message.IsCrossTenant, message.CrossTenantReason,
            message.EncryptedPayload, message.KeyId);

        db.AuditEvents.Add(ev);
        await db.SaveChangesAsync(ct);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
