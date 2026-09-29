using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.ValueObjects;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.Infrastructure.Services;

namespace NexaFlow.Api.Middleware;

/// <summary>
///     Resolves the current tenant for an HTTP request (per ADR-004 §2.2 — tenant
///     resolution layer). Strategy:
///     <list type="number">
///         <item>Read the authenticated principal's <c>sub</c> claim (JWT bearer middleware populates).</item>
///         <item>If the user is unauthenticated and the endpoint allows anonymous: no tenant — pass through.</item>
///         <item>If the user is authenticated but did not supply <c>X-Organization-Id</c>:
///             set tenant to null. Tenant-scoped commands will refuse to persist.</item>
///         <item>If the user supplied <c>X-Organization-Id</c>: <b>validate it against the user's
///             actual current organization memberships in the database</b>. Never trust the
///             header alone (section 7). Membership / role changes since the access token was
///             issued are reflected because we hit the DB on every request.</item>
///         <item>Push the resolved tenant id into <see cref="CurrentTenantService" /> for the
///             rest of the request scope.</item>
///     </list>
/// </summary>
internal sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private const string OrganizationIdHeader = "X-Organization-Id";

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        CurrentTenantService currentTenant,
        ICurrentUserService currentUser,
        ILogger<TenantResolutionMiddleware> logger)
    {
        // If the user is not authenticated, we cannot resolve a tenant — tenant-scoped
        // operations will fail-closed downstream. Skip tenant setup entirely.
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            await _next(context);
            return;
        }

        // Read the candidate organization id from the header (if any).
        if (!context.Request.Headers.TryGetValue(OrganizationIdHeader, out var headerValues)
            || string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            // Authenticated user but no tenant selection — proceed with no tenant resolved.
            // Tenant-scoped handlers will refuse to persist; tenant-scoped reads will return no rows.
            await _next(context);
            return;
        }

        var rawHeader = headerValues.ToString();
        if (!Guid.TryParse(rawHeader, out var candidateOrganizationId) || candidateOrganizationId == Guid.Empty)
        {
            // The header was supplied but malformed — return 400 Bad Request (Problem Details).
            logger.LogInformation(
                "Tenant resolution rejected: malformed {Header} header from user {UserId}.",
                OrganizationIdHeader,
                userId);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://api.nexaflow.com/errors/INVALID_TENANT_HEADER",
                title = "Invalid tenant header",
                status = 400,
                detail = $"The {OrganizationIdHeader} header must be a valid UUID.",
                errorCode = "INVALID_TENANT_HEADER",
                traceId = context.TraceIdentifier
            });
            return;
        }

        // Validate the candidate against the user's current memberships IN THE DATABASE.
        // We bypass the global query filter on OrganizationMember by going through DbContext
        // directly — we are not yet in a tenant context, and the membership table itself is
        // tenant-scoped (filter excludes rows where OrganizationId != currentTenant which is null
        // at this point). Use the IgnoreQueryFilters pattern via direct SQL or via a raw EF query.
        //
        // For Phase 2 we use a direct query: SELECT FROM organization_members WHERE user_id = ?
        //   AND organization_id = ? AND is_active = true
        // The global query filter does not apply to reads done by the tenant resolution layer
        // itself (we use AsNoTracking + a raw query that bypasses the filter).
        var membership = await dbContext.OrganizationMembers
            .AsNoTracking()
            .IgnoreQueryFilters()  // bypass tenant filter — we're resolving the tenant, not in one
            .Where(m => m.UserId == userId
                && m.OrganizationId == candidateOrganizationId
                && m.IsActive)
            .Select(m => new { m.OrganizationId, m.Role })
            .FirstOrDefaultAsync(context.RequestAborted);

        if (membership is null)
        {
            // The user is not a member of the requested organization. Return 403 — but to avoid
            // confirming that the organization exists in another tenant, return 404.
            // (Section 27 — leak-avoidance.)
            logger.LogInformation(
                "Tenant resolution rejected: user {UserId} is not a member of organization {OrgId}.",
                userId,
                candidateOrganizationId);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://api.nexaflow.com/errors/TENANT_NOT_FOUND",
                title = "Tenant not found",
                status = 404,
                detail = "The requested organization does not exist or you do not have access.",
                errorCode = "TENANT_NOT_FOUND",
                traceId = context.TraceIdentifier
            });
            return;
        }

        // Tenant resolved — push into the scoped service. Fail-closed downstream is no
        // longer possible for this request.
        currentTenant.SetTenant(TenantId.From(membership.OrganizationId));

        logger.LogInformation(
            "Tenant resolved for user {UserId}: organization {OrgId} (role: {Role}).",
            userId,
            membership.OrganizationId,
            membership.Role);

        await _next(context);
    }
}
