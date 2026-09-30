namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     Handler for a specific event type. Registered in DI; the consumer dispatches
///     messages to the appropriate handler by event type name.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
