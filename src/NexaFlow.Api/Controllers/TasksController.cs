using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Tasks.Commands;
using NexaFlow.Application.Features.Tasks.Dtos;
using NexaFlow.Application.Features.Tasks.Queries;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Task endpoints under projects. All require authentication + X-Organization-Id.
///     Resource-level authorization (project membership + role) is enforced in handlers
///     via ProjectAccess. The authorization chain is:
///     Task → Project → Organization → Resolved Tenant → User → Permission → Project Membership.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/tasks")]
[Authorize]
public sealed class TasksController : ControllerBase
{
    private readonly IMediator _mediator;

    public TasksController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TaskSummaryDto>>> ListAsync(
        Guid projectId,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] TaskPriority? priority,
        [FromQuery] Guid? assigneeId,
        [FromQuery] DateTimeOffset? dueBefore,
        [FromQuery] DateTimeOffset? dueAfter,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetTasksQuery(
            projectId, status, priority, assigneeId, dueBefore, dueAfter,
            sortBy, sortDescending, page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TaskDto>> CreateAsync(
        Guid projectId, [FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var command = new CreateTaskCommand(
            projectId, request.Title, request.Description,
            request.Priority, request.AssigneeId, request.DueDateUtc);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet("{taskId:guid}")]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> GetAsync(Guid projectId, Guid taskId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTaskQuery(projectId, taskId), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid projectId, Guid taskId, [FromBody] UpdateTaskRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdateTaskCommand(
            projectId, taskId, request.NewTitle, request.NewDescription,
            request.NewPriority, request.DueDateUtc, request.UpdateDueDate), ct);
        return NoContent();
    }

    [HttpPost("{taskId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatusAsync(
        Guid projectId, Guid taskId, [FromBody] ChangeTaskStatusRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ChangeTaskStatusCommand(projectId, taskId, request.NewStatus), ct);
        return NoContent();
    }

    [HttpPost("{taskId:guid}/assignee")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignAsync(
        Guid projectId, Guid taskId, [FromBody] AssignTaskRequest request, CancellationToken ct)
    {
        await _mediator.Send(new AssignTaskCommand(projectId, taskId, request.AssigneeId), ct);
        return NoContent();
    }

    [HttpDelete("{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid projectId, Guid taskId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteTaskCommand(projectId, taskId), ct);
        return NoContent();
    }
}

public sealed record CreateTaskRequest(
    string Title,
    string Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateTimeOffset? DueDateUtc);

public sealed record UpdateTaskRequest(
    string? NewTitle,
    string? NewDescription,
    TaskPriority? NewPriority,
    DateTimeOffset? DueDateUtc,
    bool UpdateDueDate);

public sealed record ChangeTaskStatusRequest(TaskItemStatus NewStatus);
public sealed record AssignTaskRequest(Guid? AssigneeId);
