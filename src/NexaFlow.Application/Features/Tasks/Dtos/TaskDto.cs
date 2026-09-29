using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Tasks.Dtos;

public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    Guid? AssigneeId,
    Guid ReporterId,
    DateTimeOffset? DueDateUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TaskSummaryDto(
    Guid Id,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateTimeOffset? DueDateUtc);
