using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Application.Features.Projects.Dtos;
using NexaFlow.Application.Features.Projects.Queries;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Project management endpoints. All endpoints require:
/// - Authentication (JWT bearer)
/// - X-Organization-Id header (validated by TenantResolutionMiddleware against the user's
///   current org membership — the database is authoritative, NOT the JWT)
/// - Organization-level permission for create/list (project.create / project.read)
/// - Resource-level authorization for update/delete/get-specific (checked in the handler
///   via ProjectAccess — the user must be a member of the specific project with the
///   required project role)
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectsController(IMediator mediator) => _mediator = mediator;

    /// <summary>List projects in the resolved tenant that the user is a member of. Paged + filtered + sorted.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.ProjectRead)]
    [ProducesResponseType(typeof(PagedResult<ProjectSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectSummaryDto>>> ListAsync(
        [FromQuery] ProjectStatus? status,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectsQuery(status, search, sortBy, sortDescending, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Create a new project. The current user becomes the project Owner. OrganizationId comes from the resolved tenant — never from the request body.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.ProjectCreate)]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProjectDto>> CreateAsync(
        [FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        var command = new CreateProjectCommand(request.Name, request.Description, request.StartDateUtc, request.DueDateUtc);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Get a single project. Returns 404 if not found OR if the user is not a member (no enumeration leak).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectQuery(id), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>Update a project's editable fields (name, description, status, dates). Partial — null means leave unchanged. Requires project Contributor role (resource-level).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        var dates = request.UpdateDates
            ? new DatesUpdate(request.StartDateUtc, request.DueDateUtc)
            : null;
        await _mediator.Send(new UpdateProjectCommand(id, request.NewName, request.NewDescription, request.NewStatus, dates), ct);
        return NoContent();
    }

    /// <summary>Soft-delete a project. Requires project Owner role (resource-level). Memberships retained for audit.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteProjectCommand(id), ct);
        return NoContent();
    }
}

// --- Request DTOs (API layer — map 1:1 to commands) ---

public sealed record CreateProjectRequest(
    string Name,
    string Description,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? DueDateUtc);

public sealed record UpdateProjectRequest(
    string? NewName,
    string? NewDescription,
    ProjectStatus? NewStatus,
    bool UpdateDates,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? DueDateUtc);
