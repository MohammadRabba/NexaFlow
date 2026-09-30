using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Organizations;

public sealed record OrganizationCreatedEvent(
    Guid EventId,
    Guid OrganizationId,
    Guid OwnerUserId,
    string Name,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
