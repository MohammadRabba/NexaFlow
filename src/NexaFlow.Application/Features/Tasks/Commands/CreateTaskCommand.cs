using MediatR;
using NexaFlow.Application.Features.Tasks.Dtos;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Tasks.Commands;

/// <summary>
///     Create a task. ProjectId comes from the URL route (validated by ProjectAccess).
///     ReporterId is the authenticated current user — never from the request body.
///     OrganizationId is derived from the resolved tenant — never from the request body.
/// </summary>
public sealed record CreateTaskCommand(
    Guid ProjectId,
    string Title,
    string Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateTimeOffset? DueDateUtc) : IRequest<TaskDto>;
