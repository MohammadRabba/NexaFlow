using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     RabbitMQ consumer with reconnection. Implements IHostedService. On connection loss,
///     disposes broken channel/connection, waits with configurable backoff, and reconnects.
///     At-least-once delivery — unacked messages are requeued on connection loss.
/// </summary>
/// <remarks>
///     ACK/NACK semantics:
///     - Handler success → ACK (message removed from queue)
///     - Handler failure → NACK + requeue (message redelivered)
///     - Invalid JSON / unknown type → ACK (discard — poison message, don't retry)
///     - DB exception → NACK + requeue (transient, retry)
///     - Duplicate notification → handler catches constraint violation, returns normally → ACK
///     - SignalR failure → notification already persisted → ACK (best-effort delivery)
///     - Connection loss → unacked messages requeued by RabbitMQ automatically
///     - Shutdown → cancel consumer; unacked messages requeued by RabbitMQ
/// </remarks>
public sealed class RabbitMqConsumer : IHostedService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;
    private CancellationTokenSource? _reconnectCts;
    private bool _disposed;

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
        _reconnectCts = new CancellationTokenSource();
        _ = ReconnectLoopAsync(_reconnectCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_reconnectCts is not null)
            await _reconnectCts.CancelAsync();
        await CleanupConnectionAsync();
        _logger.LogInformation("RabbitMqConsumer stopped.");
    }

    /// <summary>
    ///     Reconnection loop: attempts to connect, starts consuming, and on connection loss
    ///     disposes broken resources and retries after backoff. Runs until cancelled.
    /// </summary>
    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var backoff = _options.ReconnectInitialDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(cancellationToken);

                // Wait until connection is lost or shutdown is requested.
                // The connection's Shutdown event will unblock this.
                await WaitForConnectionLossAsync(cancellationToken);
                backoff = _options.ReconnectInitialDelay; // reset on graceful disconnect
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RabbitMQ connection failed. Retrying in {Backoff}s.",
                    backoff.TotalSeconds);

                await CleanupConnectionAsync();
                try
                {
                    await Task.Delay(backoff, cancellationToken);
                }
                catch (OperationCanceledException) { break; }

                backoff = backoff * 2 < _options.ReconnectMaxDelay ? backoff * 2 : _options.ReconnectMaxDelay;
            }
        }
    }

    private async Task WaitForConnectionLossAsync(CancellationToken cancellationToken)
    {
        if (_connection is null) return;
        var tcs = new TaskCompletionSource();

        // RabbitMQ.Client 7.x: IConnection has a ConnectionShutdownAsync event
        _connection.ConnectionShutdownAsync += (_, _) =>
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        using var registration = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }

    private async Task ConnectAndConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10)
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

        _logger.LogInformation("RabbitMqConsumer connected. Queue: {Queue}.", _options.NotificationQueueName);
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
                // Poison message — ACK to discard. Retrying won't fix invalid JSON
                // or unknown type.
                _logger.LogWarning("Discarding poison message: {EventType}.", eventType);
                if (_channel is not null && _channel.IsOpen)
                    await _channel.BasicAckAsync(deliveryTag, false);
                return;
            }

            var handleMethod = handler.GetType().GetMethod("HandleAsync");
            if (handleMethod is null)
            {
                _logger.LogError("Handler {HandlerType} has no HandleAsync.", handler.GetType().Name);
                if (_channel is not null && _channel.IsOpen)
                    await _channel.BasicNackAsync(deliveryTag, false, true);
                return;
            }

            var task = (Task)handleMethod.Invoke(handler, [@event, CancellationToken.None])!;
            await task;

            // Handler succeeded (including idempotent duplicate) → ACK.
            if (_channel is not null && _channel.IsOpen)
                await _channel.BasicAckAsync(deliveryTag, false);

            _logger.LogInformation("Processed {EventType}, tag {Tag}.", eventType, deliveryTag);
        }
        catch (Exception ex)
        {
            // Handler failed (DB exception, timeout, etc.) → NACK + requeue.
            _logger.LogError(ex, "Handler failed for {EventType}, tag {Tag} — requeueing.", eventType, deliveryTag);
            if (_channel is not null && _channel.IsOpen)
            {
                try { await _channel.BasicNackAsync(deliveryTag, false, true); }
                catch (Exception nackEx) { _logger.LogWarning(nackEx, "NACK failed — message may be requeued on reconnect."); }
            }
        }
    }

    private (object? Handler, object? Event) ResolveHandler(IServiceScope scope, string eventType, string payload)
    {
        var eventTypeObj = Type.GetType(eventType, throwOnError: false);
        if (eventTypeObj is null)
        {
            _logger.LogWarning("Unknown event type {EventType}.", eventType);
            return (null, null);
        }

        if (!eventTypeObj.Namespace?.StartsWith("NexaFlow.Domain") == true)
        {
            _logger.LogWarning("Event type {EventType} not from NexaFlow.Domain — rejecting.", eventType);
            return (null, null);
        }

        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventTypeObj);
        var handler = scope.ServiceProvider.GetService(handlerInterface);
        if (handler is null)
        {
            _logger.LogWarning("No IEventHandler<{EventType}> registered.", eventType);
            return (null, null);
        }

        try
        {
            var @event = JsonSerializer.Deserialize(payload, eventTypeObj, WebOptions);
            return @event is null ? (handler, null) : (handler, @event);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization failed for {EventType}.", eventType);
            return (handler, null);
        }
    }

    private async Task CleanupConnectionAsync()
    {
        try
        {
            if (_channel is not null) { await _channel.DisposeAsync(); _channel = null; }
            if (_connection is not null) { await _connection.DisposeAsync(); _connection = null; }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up RabbitMQ connection.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_reconnectCts is not null)
            await _reconnectCts.CancelAsync();
        _reconnectCts?.Dispose();
        await CleanupConnectionAsync();
    }
}
