namespace NexaFlow.Domain.Common;

/// <summary>
///     Marker interface for domain events raised by aggregates / entities.
///     Domain events represent "something meaningful that happened in the domain"
///     (section 11). They are collected on the entity that raised them and dispatched
///     by the Application / Infrastructure layer after SaveChanges succeeds.
///     <para>
///         Phase 1: this interface exists so that entities can carry events from day one,
///         giving us the right shape. The dispatcher abstraction is introduced in Phase 6
///         (RabbitMQ + Outbox). No events are dispatched in Phase 1.
///     </para>
/// </summary>
public interface IDomainEvent
{
    /// <summary>UTC timestamp at which the event was raised.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}
