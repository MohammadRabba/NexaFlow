using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Infrastructure.Persistence;

namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     Background service that polls the outbox table for unpublished messages and publishes
///     them to RabbitMQ via <see cref="IMessageBusPublisher" />. At-least-once delivery.
/// </summary>
/// <remarks>
///     Multi-instance safe: uses PostgreSQL's <c>FOR UPDATE SKIP LOCKED</c> via
///     <c>FromSqlRaw</c> to claim messages atomically. Two concurrent processors
///     will never process the same message.
/// </remarks>
public sealed class OutboxProcessor : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageBusPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;
    private Timer? _timer;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IMessageBusPublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OutboxProcessor started. Polling every {Interval}s.",
            _options.PollingInterval.TotalSeconds);
        _timer = new Timer(ProcessPendingAsync, null, TimeSpan.Zero, _options.PollingInterval);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OutboxProcessor stopping.");
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }
        _logger.LogInformation("OutboxProcessor stopped.");
    }

    private async void ProcessPendingAsync(object? state)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // PostgreSQL FOR UPDATE SKIP LOCKED — atomically claims pending messages.
            // Two concurrent processors (different instances) will each get a distinct set.
            // EF Core translates .FromSqlRaw with parameterized LINQ correctly.
            var batch = await db.OutboxMessages
                .FromSqlRaw("""
                    SELECT * FROM outbox_messages
                    WHERE processed_on_utc IS NULL
                    ORDER BY occurred_on_utc
                    LIMIT {0}
                    FOR UPDATE SKIP LOCKED
                    """, _options.BatchSize)
                .ToListAsync();

            if (batch.Count == 0) return;

            _logger.LogInformation("Processing {Count} outbox messages.", batch.Count);

            foreach (var msg in batch)
            {
                try
                {
                    var published = await _publisher.PublishAsync(msg.EventType, msg.Payload);
                    if (published)
                    {
                        msg.ProcessedOnUtc = DateTimeOffset.UtcNow;
                        _logger.LogInformation("Outbox message {Id} published.", msg.Id);
                    }
                    else
                    {
                        msg.AttemptCount++;
                        msg.Error = "Publisher returned false.";
                        _logger.LogWarning("Outbox message {Id} publish returned false.", msg.Id);
                    }
                }
                catch (Exception ex)
                {
                    msg.AttemptCount++;
                    msg.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    _logger.LogError(ex, "Failed to publish outbox message {Id}.", msg.Id);
                }
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OutboxProcessor error during batch processing.");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

public sealed class OutboxOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int BatchSize { get; set; } = 50;
}
