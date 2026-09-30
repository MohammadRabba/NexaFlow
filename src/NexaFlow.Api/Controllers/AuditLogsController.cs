using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaFlow.Application.Authorization;
using NexaFlow.Application.Features.AuditLogs.Queries;

namespace NexaFlow.Api.Controllers;

/// <summary>
///     Read-only access to the audit log (spec §25, §28). Two endpoints:
///     <list type="bullet">
///         <item>
///             <c>GET /api/audit-logs</c> — organization-scoped view, requires the
///             <see cref="Permissions.AuditLogRead" /> permission (granted to Owner
///             and Admin roles per <see cref="RolePermissions" />).
///         </item>
///         <item>
///             <c>GET /api/audit-logs/my-activity</c> — self-service "my login activity"
///             view (auth events: login, logout, password change, email verification).
///             Available to any authenticated user for their own activity. The handler
///             reads the user id from the ambient <c>ICurrentUserService</c> — the
///             client never supplies it.
///         </item>
///     </list>
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuditLogsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    ///     Organization-scoped audit log. Filters: action, userId, entity, entityId,
    ///     fromUtc, toUtc. Sorted by timestamp descending. Page sizes are clamped to
    ///     [1, 100] by the handler.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.AuditLogRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOrganizationAsync(
        [FromQuery] string? action = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? entity = null,
        [FromQuery] Guid? entityId = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new GetAuditLogsQuery(
            Action: action,
            UserId: userId,
            Entity: entity,
            EntityId: entityId,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Scope: "organization",
            Page: page,
            PageSize: pageSize);

        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    ///     Self-service view of the current user's auth history (login, logout,
    ///     password changes, email verification, refresh-token rotation). Cross-tenant:
    ///     includes all audit rows where <c>UserId</c> equals the current user, regardless
    ///     of which organization (if any) was active at the time.
    /// </summary>
    [HttpGet("my-activity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyActivityAsync(
        [FromQuery] string? action = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new GetAuditLogsQuery(
            Action: action,
            UserId: null, // the handler ignores this for scope=user and uses the ambient user
            Entity: null,
            EntityId: null,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Scope: "user",
            Page: page,
            PageSize: pageSize);

        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }
}
