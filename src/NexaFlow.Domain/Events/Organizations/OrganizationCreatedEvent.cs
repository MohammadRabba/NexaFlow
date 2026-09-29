using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Organizations;

/// <summary>
///     Raised when a new organization is created. This is a meaningful business event
///     (section 11) — not a trivial property change. The owner membership is part of
///     the same aggregate transaction; the event signals "a new tenant exists".
/// </summary>
public sealed record OrganizationCreatedEvent(
    Guid OrganizationId,
    Guid OwnerUserId,
    string Name,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
