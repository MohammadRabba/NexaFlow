using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.ProjectMembers.Commands;
using NexaFlow.Application.Features.ProjectMembers.Dtos;
using NexaFlow.Application.Features.ProjectMembers.Queries;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Project member management endpoints. All endpoints require authentication +
/// X-Organization-Id header. Resource-level authorization (the user must be a member
/// of the specific project with the required role) is enforced in the handlers via
/// the shared ProjectAccess helper.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/members")]
[Authorize]
public sealed class ProjectMembersController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectMembersController(IMediator mediator) => _mediator = mediator;

    /// <summary>List the members of a project. Requires Reader role minimum.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProjectMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ProjectMemberDto>>> ListAsync(
        Guid projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectMembersQuery(projectId, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Add a member to the project. Requires project Owner role.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProjectMemberDto>> AddAsync(
        Guid projectId, [FromBody] AddProjectMemberRequest request, CancellationToken ct)
    {
        var command = new AddProjectMemberCommand(projectId, request.UserId, request.Role);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Change a member's role. Requires project Owner role.</summary>
    [HttpPut("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateRoleAsync(
        Guid projectId, Guid userId, [FromBody] UpdateProjectMemberRoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdateProjectMemberRoleCommand(projectId, userId, request.NewRole), ct);
        return NoContent();
    }

    /// <summary>Remove a member. Contributors can self-remove; only Owner can remove others. Owner cannot be removed (must transfer first).</summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(
        Guid projectId, Guid userId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveProjectMemberCommand(projectId, userId), ct);
        return NoContent();
    }

    /// <summary>Transfer project ownership. Only the current Owner can call. The old Owner becomes a Contributor.</summary>
    [HttpPost("transfer-ownership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransferOwnershipAsync(
        Guid projectId, [FromBody] TransferProjectOwnershipRequest request, CancellationToken ct)
    {
        await _mediator.Send(new TransferProjectOwnershipCommand(projectId, request.ToUserId), ct);
        return NoContent();
    }
}

public sealed record AddProjectMemberRequest(Guid UserId, ProjectMemberRole Role);
public sealed record UpdateProjectMemberRoleRequest(ProjectMemberRole NewRole);
public sealed record TransferProjectOwnershipRequest(Guid ToUserId);
