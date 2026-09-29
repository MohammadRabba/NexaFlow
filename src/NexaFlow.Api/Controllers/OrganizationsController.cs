using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Organizations.Commands;
using NexaFlow.Application.Features.Organizations.Dtos;
using NexaFlow.Application.Features.Organizations.Queries;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Organization management endpoints. The current user becomes the Owner when
///     creating an organization. Subsequent operations are scoped by the resolved
///     tenant (the X-Organization-Id header, validated against the user's memberships
///     by the TenantResolutionMiddleware).
/// </summary>
[ApiController]
[Route("api/organizations")]
[Authorize]  // All endpoints require authentication
public sealed class OrganizationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>List the organizations the current user belongs to. (No tenant header required.)</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrganizationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrganizationSummaryDto>>> ListAsync(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrganizationsQuery(page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Create a new organization. The current user becomes the Owner.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrganizationDto>> CreateAsync(
        [FromBody] CreateOrganizationRequest request, CancellationToken ct)
    {
        var command = new CreateOrganizationCommand(request.Name, request.SlugSuggestion);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Get a single organization by id. Returns 404 if not found OR not a member.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetOrganizationQuery(id), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>Rename an organization (slug is immutable). Requires OrganizationUpdate permission.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.OrganizationUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateOrganizationRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdateOrganizationCommand(id, request.NewName), ct);
        return NoContent();
    }

    /// <summary>Soft-delete an organization. Requires OrganizationDelete permission (Owner only).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.OrganizationDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteOrganizationCommand(id), ct);
        return NoContent();
    }
}

public sealed record CreateOrganizationRequest(string Name, string? SlugSuggestion);
public sealed record UpdateOrganizationRequest(string NewName);
