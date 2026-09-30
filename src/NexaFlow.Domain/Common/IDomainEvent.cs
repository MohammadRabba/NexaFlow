namespace NexaFlow.Domain.Common;

/// <summary>
///     Marker interface for domain events. Each event carries a stable <see cref="EventId" />
///     (generated at construction) for consumer-side idempotency under at-least-once delivery.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Stable unique identifier for this event instance. Used by consumers for deduplication.</summary>
    Guid EventId { get; }

    /// <summary>UTC timestamp at which the event was raised.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}
