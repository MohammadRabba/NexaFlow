using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Tasks;

public sealed record TaskCreatedEvent(
    Guid EventId,
    Guid TaskId,
    Guid ProjectId,
    Guid OrganizationId,
    Guid ReporterId,
    string Title,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record TaskAssignedEvent(
    Guid EventId,
    Guid TaskId,
    Guid OrganizationId,
    string TaskTitle,
    Guid? AssigneeId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record TaskStatusChangedEvent(
    Guid EventId,
    Guid TaskId,
    string FromStatus,
    string ToStatus,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record TaskCommentAddedEvent(
    Guid EventId,
    Guid TaskId,
    Guid CommentId,
    Guid AuthorId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
