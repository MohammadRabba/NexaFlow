using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Features.Comments.Commands;

namespace NexaFlow.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/tasks/{taskId:guid}/comments")]
[Authorize]
public sealed class CommentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CommentsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(List<Domain.Entities.Comment>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Domain.Entities.Comment>>> ListAsync(
        Guid projectId, Guid taskId, CancellationToken ct)
    {
        var comments = await _mediator.Send(new GetCommentsQuery(projectId, taskId), ct);
        return Ok(comments);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Domain.Entities.Comment), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Domain.Entities.Comment>> CreateAsync(
        Guid projectId, Guid taskId, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var comment = await _mediator.Send(new CreateCommentCommand(projectId, taskId, request.Body), ct);
        return Ok(comment);
    }

    [HttpPut("{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditAsync(
        Guid projectId, Guid taskId, Guid commentId, [FromBody] EditCommentRequest request, CancellationToken ct)
    {
        await _mediator.Send(new EditCommentCommand(projectId, taskId, commentId, request.NewBody), ct);
        return NoContent();
    }

    [HttpDelete("{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(
        Guid projectId, Guid taskId, Guid commentId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteCommentCommand(projectId, taskId, commentId), ct);
        return NoContent();
    }
}

public sealed record CreateCommentRequest(string Body);
public sealed record EditCommentRequest(string NewBody);
