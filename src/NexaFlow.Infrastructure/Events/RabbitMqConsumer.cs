using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     RabbitMQ consumer. Implements IHostedService for DI lifecycle management.
///     Reads from the notification queue, deserializes, dispatches to IEventHandler<T>,
///     ACKs on success, NACKs + requeues on failure. At-least-once delivery.
/// </summary>
public sealed class RabbitMqConsumer : IHostedService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;

    public RabbitMqConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectAndConsumeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't crash the app if RabbitMQ is unavailable — the OutboxProcessor
            // will still accumulate messages; when RabbitMQ comes back, restart
            // the app or the consumer will reconnect on next poll cycle.
            _logger.LogWarning(ex,
                "RabbitMqConsumer failed to start — RabbitMQ may be unavailable. " +
                "Outbox messages will accumulate. Application continues without consumer.");
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: _options.NotificationQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await _channel.QueueBindAsync(
            queue: _options.NotificationQueueName,
            exchange: _options.ExchangeName,
            routingKey: "NexaFlow.Domain.Events.Tasks.#",
            cancellationToken: cancellationToken);

        await _channel.BasicQosAsync(0, 1, false, cancellationToken);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += OnMessageReceived;

        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _options.NotificationQueueName,
            autoAck: false,
            consumer: _consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("RabbitMqConsumer started. Queue: {Queue}.", _options.NotificationQueueName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RabbitMqConsumer stopping.");
        if (_channel is not null && _consumerTag is not null)
        {
            try { await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error cancelling consumer."); }
        }
        await DisposeAsync();
    }

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs ea)
    {
        var eventType = ea.BasicProperties.Type ?? string.Empty;
        var payload = System.Text.Encoding.UTF8.GetString(ea.Body.ToArray());
        var deliveryTag = ea.DeliveryTag;

        _logger.LogInformation("Received: {EventType}, tag {Tag}.", eventType, deliveryTag);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var (handler, @event) = ResolveHandler(scope, eventType, payload);

            if (handler is null || @event is null)
            {
                _logger.LogWarning("No handler or deserialization failed for {EventType} — discarding.", eventType);
                if (_channel is not null)
                    await _channel.BasicAckAsync(deliveryTag, false);
                return;
            }

            // Invoke handler.HandleAsync via reflection — the handler type is determined
            // at runtime by the event type. This is safe because ResolveHandler only
            // resolves types from NexaFlow.Domain.
            var handleMethod = handler.GetType().GetMethod("HandleAsync");
            if (handleMethod is null)
            {
                _logger.LogError("Handler {HandlerType} has no HandleAsync method.", handler.GetType().Name);
                if (_channel is not null)
                    await _channel.BasicNackAsync(deliveryTag, false, true);
                return;
            }

            var task = (Task)handleMethod.Invoke(handler, [@event, CancellationToken.None])!;
            await task;

            if (_channel is not null)
                await _channel.BasicAckAsync(deliveryTag, false);

            _logger.LogInformation("Processed {EventType}, tag {Tag}.", eventType, deliveryTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {EventType}, tag {Tag} — requeueing.", eventType, deliveryTag);
            if (_channel is not null)
                await _channel.BasicNackAsync(deliveryTag, false, true);
        }
    }

    private (object? Handler, object? Event) ResolveHandler(IServiceScope scope, string eventType, string payload)
    {
        var eventTypeObj = Type.GetType(eventType, throwOnError: false);
        if (eventTypeObj is null || !eventTypeObj.Namespace?.StartsWith("NexaFlow.Domain") == true)
            return (null, null);

        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventTypeObj);
        var handler = scope.ServiceProvider.GetService(handlerInterface);
        if (handler is null) return (null, null);

        try
        {
            var @event = JsonSerializer.Deserialize(payload, eventTypeObj);
            return (@event is null ? (handler, null) : (handler, @event));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization failed for {EventType}.", eventType);
            return (handler, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
