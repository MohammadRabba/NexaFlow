using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Members.Commands;
using NexaFlow.Application.Features.Members.Dtos;
using NexaFlow.Application.Features.Members.Queries;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Organization membership endpoints. All endpoints require the X-Organization-Id header
///     (validated against the user's memberships by the TenantResolutionMiddleware).
/// </summary>
[ApiController]
[Route("api/organizations/{organizationId:guid}/members")]
[Authorize]
public sealed class OrganizationMembersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationMembersController(IMediator mediator) => _mediator = mediator;

    /// <summary>List the active members of the organization. Requires MemberRead permission.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.MemberRead)]
    [ProducesResponseType(typeof(PagedResult<MemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MemberDto>>> ListAsync(
        Guid organizationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMembersQuery(organizationId, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>
    ///     Invite a user by email. The invitee must already be registered. The invitation
    ///     creates a pending membership with a hashed token (plaintext returned once in the
    ///     response — never stored, never logged). Requires MemberInvite permission.
    /// </summary>
    [HttpPost("invite")]
    [Authorize(Policy = Permissions.MemberInvite)]
    [ProducesResponseType(typeof(InviteSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InviteSummaryDto>> InviteAsync(
        Guid organizationId, [FromBody] InviteMemberRequest request, CancellationToken ct)
    {
        var command = new InviteMemberCommand(organizationId, request.InviteeEmail, request.Role);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    ///     Change a member's role. The target's current role determines what's allowed:
    ///     Owner can change anyone (except Owners — use transfer-ownership); Admin can
    ///     change Member/Viewer but NOT another Admin. Requires MemberUpdate permission.
    /// </summary>
    [HttpPut("{userId:guid}")]
    [Authorize(Policy = Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateRoleAsync(
        Guid organizationId, Guid userId, [FromBody] UpdateMemberRoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(
            new UpdateMemberRoleCommand(organizationId, userId, request.NewRole), ct);
        return NoContent();
    }

    /// <summary>
    ///     Remove a member. Owner cannot self-remove (must transfer ownership first).
    ///     Self-removal is allowed for non-Owners. Removing someone else requires
    ///     Owner or Admin role; Admin cannot remove another Admin. Requires MemberRemove
    ///     permission (which Owner and Admin hold).
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [Authorize(Policy = Permissions.MemberRemove)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(
        Guid organizationId, Guid userId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveMemberCommand(organizationId, userId), ct);
        return NoContent();
    }

    /// <summary>
    ///     Transfer ownership to an existing member. The current Owner becomes an Admin.
    ///     Requires MemberTransferOwnership permission (only Owner holds it).
    /// </summary>
    [HttpPost("transfer-ownership")]
    [Authorize(Policy = Permissions.MemberTransferOwnership)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransferOwnershipAsync(
        Guid organizationId, [FromBody] TransferOwnershipRequest request, CancellationToken ct)
    {
        await _mediator.Send(new TransferOwnershipCommand(organizationId, request.ToUserId), ct);
        return NoContent();
    }
}

public sealed record InviteMemberRequest(string InviteeEmail, OrganizationRole Role);
public sealed record UpdateMemberRoleRequest(OrganizationRole NewRole);
public sealed record TransferOwnershipRequest(Guid ToUserId);
