using NexaFlow.Domain.Common;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction for publishing integration events after a transaction commits
///     (section 22 — Outbox Pattern). <b>DEFERRED</b> — Phase 6 implements this
///     via the outbox table + a background publisher to RabbitMQ.
///     <para>
///         The contract is here so handlers in Phase 6+ can depend on it without
///         restructuring. No DI registration in Phase 1 — registering a noop that
///         swallows messages would be a fake implementation.
///     </para>
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    ///     Enqueue an integration event for publication after the current transaction commits.
    ///     Implementations MUST guarantee at-least-once delivery (Outbox + acknowledgment).
    /// </summary>
    Task PublishAsync<TIntegrationEvent>(
        TIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : IDomainEvent;
}
