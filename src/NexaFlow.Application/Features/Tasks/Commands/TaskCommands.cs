using MediatR;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Tasks.Commands;

public sealed record UpdateTaskCommand(
    Guid ProjectId,
    Guid TaskId,
    string? NewTitle,
    string? NewDescription,
    TaskPriority? NewPriority,
    DateTimeOffset? DueDateUtc,
    bool UpdateDueDate) : IRequest<Unit>;

public sealed record ChangeTaskStatusCommand(
    Guid ProjectId,
    Guid TaskId,
    TaskItemStatus NewStatus) : IRequest<Unit>;

public sealed record AssignTaskCommand(
    Guid ProjectId,
    Guid TaskId,
    Guid? AssigneeId) : IRequest<Unit>;

public sealed record DeleteTaskCommand(
    Guid ProjectId,
    Guid TaskId) : IRequest<Unit>;
