using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace DeveloperPlatform.Infrastructure.Messaging;

public sealed class RabbitMqPublisher : IAsyncDisposable
{
    private const string ExchangeName = "developer-platform.audit";
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task InitializeAsync(string hostName, CancellationToken ct = default)
    {
        var factory = new ConnectionFactory { HostName = hostName };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: ct);
    }

    public async Task PublishAsync(AuditMessage message, CancellationToken ct = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Publisher not initialized.");
        }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var routingKey = $"audit.{message.TenantId}";

        var props = new BasicProperties
        {
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                ["x-tenant-id"] = message.TenantId.ToString(),
                ["x-command-type"] = message.CommandType
            }
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
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
