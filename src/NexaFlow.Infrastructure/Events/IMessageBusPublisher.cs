namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     Abstraction for publishing a serialized outbox message to the message broker.
///     Infrastructure provides a RabbitMQ implementation; tests can provide a fake.
/// </summary>
public interface IMessageBusPublisher
{
    Task<bool> PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default);
}
