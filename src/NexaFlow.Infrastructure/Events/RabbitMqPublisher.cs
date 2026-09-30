using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     RabbitMQ publisher — connects to RabbitMQ, declares a durable exchange, and publishes
///     messages with persistent delivery. Reconnects on failure. Connection management is
///     lazy (first publish triggers connection). Channel is per-publish (simpler than
///     channel pooling for Phase 6's volume).
/// </summary>
public sealed class RabbitMqPublisher : IMessageBusPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IChannel? channel = null;
        try
        {
            channel = await GetOrCreateChannelAsync(cancellationToken);

            var body = Encoding.UTF8.GetBytes(payload);
            var props = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json",
                Type = eventType
            };

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: eventType,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ publish failed for {EventType}.", eventType);
            return false;
        }
        finally
        {
            if (channel is not null)
                await channel.DisposeAsync();
        }
    }

    private async Task<IChannel> GetOrCreateChannelAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        var channel = await _connection!.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        return channel;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connection is not null && _connection.IsOpen) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is not null && _connection.IsOpen) return;

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _logger.LogInformation("RabbitMQ connected to {Host}:{Port}.", _options.Host, _options.Port);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _connection?.Dispose();
        _lock.Dispose();
    }
}

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "nexaflow.events";
    public string NotificationQueueName { get; set; } = "nexaflow.notifications";
    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromMinutes(1);
}
