using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Events.Tasks;

public sealed record TaskCreatedEvent(
    Guid TaskId, Guid ProjectId, Guid OrganizationId, Guid ReporterId, string Title, DateTimeOffset OccurredOnUtc)
    : IDomainEvent;

public sealed record TaskAssignedEvent(
    Guid TaskId, Guid? AssigneeId, DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record TaskStatusChangedEvent(
    Guid TaskId, string FromStatus, string ToStatus, DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record TaskCommentAddedEvent(
    Guid TaskId, Guid CommentId, Guid AuthorId, DateTimeOffset OccurredOnUtc) : IDomainEvent;
