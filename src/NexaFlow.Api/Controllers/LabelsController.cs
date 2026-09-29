using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Features.Labels.Commands;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Api.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:guid}/labels")]
[Authorize]
public sealed class LabelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LabelsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(List<Label>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Label>>> ListAsync(
        Guid organizationId, CancellationToken ct)
    {
        var labels = await _mediator.Send(new GetLabelsQuery(organizationId), ct);
        return Ok(labels);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Label), StatusCodes.Status200OK)]
    public async Task<ActionResult<Label>> CreateAsync(
        Guid organizationId, [FromBody] CreateLabelRequest request, CancellationToken ct)
    {
        var label = await _mediator.Send(new CreateLabelCommand(request.Name, request.Color), ct);
        return Ok(label);
    }

    [HttpDelete("{labelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(
        Guid organizationId, Guid labelId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteLabelCommand(labelId), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/projects/{projectId:guid}/tasks/{taskId:guid}/labels")]
[Authorize]
public sealed class TaskLabelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TaskLabelsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("{labelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AttachAsync(
        Guid projectId, Guid taskId, Guid labelId, CancellationToken ct)
    {
        await _mediator.Send(new AttachLabelCommand(projectId, taskId, labelId), ct);
        return NoContent();
    }

    [HttpDelete("{labelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DetachAsync(
        Guid projectId, Guid taskId, Guid labelId, CancellationToken ct)
    {
        await _mediator.Send(new DetachLabelCommand(projectId, taskId, labelId), ct);
        return NoContent();
    }
}

public sealed record CreateLabelRequest(string Name, string? Color);
