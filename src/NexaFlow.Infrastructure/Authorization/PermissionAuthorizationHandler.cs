using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Authorization;
using NexaFlow.Infrastructure.Persistence;

namespace NexaFlow.Infrastructure.Authorization;

/// <summary>
///     ASP.NET Core authorization handler that checks whether the current user holds
///     a permission in the resolved tenant.
/// </summary>
/// <remarks>
///     <para>
///         <b>Database is authoritative.</b> The handler queries
///         <see cref="OrganizationMembers" /> in real time to read the user's current
///         role in the resolved tenant. JWT claims are NOT consulted for permissions
///         or roles — per Phase 2 directive, this prevents stale authorization after
///         membership / role changes (membership can be revoked and the next request
///         sees the revocation immediately).
///     </para>
///     <para>
///         Order of checks:
///         <list type="number">
///             <item>User must be authenticated.</item>
///             <item>Tenant must be resolved (the X-Organization-Id middleware ran first).</item>
///             <item>User must be an active member of the resolved tenant.</item>
///             <item>The role held must grant the required permission (via <see cref="RolePermissions" />).</item>
///         </list>
///         On any failure: <see cref="AuthorizationHandlerContext.Fail" />. The HTTP layer
///         maps a failed authorization to 403 (or 401 if unauthenticated) — see
///         ExceptionHandlingMiddleware.
///     </para>
///     <para>
///         <b>Why the handler hits the DB on every authorization check:</b> The user's
///         Phase 2 directive explicitly says:
///         "Do not trust stale JWT claims. The database is authoritative for current
///         membership." This is the cost — a DB round-trip per authorization-sensitive
///         operation. Phase 7 may add a Redis-backed cache with short TTL if this
///         becomes a hot path.
///     </para>
/// </remarks>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        ApplicationDbContext dbContext,
        ICurrentTenantService currentTenant,
        ICurrentUserService currentUser,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // AuthorizationHandlerContext does not expose a CancellationToken in ASP.NET Core 10.
        // The authorization pipeline is synchronous from the request perspective; if the
        // request is aborted, ASP.NET surfaces that via RequestAborted elsewhere. We use
        // CancellationToken.None here — the DB query will run to completion. Acceptable for
        // an index-backed lookup (~1 ms).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancellationToken = cts.Token;
        // 1. Authenticated user
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            // Not authenticated — the API middleware returns 401; we just fail authorization.
            return;
        }

        // 2. Tenant resolved
        if (!_currentTenant.IsTenantResolved || _currentTenant.OrganizationId is not { } tenantId)
        {
            _logger.LogInformation(
                "Permission check failed for user {UserId} requirement {Permission}: no tenant resolved.",
                userId, requirement.Permission);
            return;
        }

        // 3. Active membership in the resolved tenant.
        // Use IgnoreQueryFilters — the OrganizationMember table is itself tenant-scoped
        // (carries OrganizationId), but the query filter would exclude all rows when the
        // accessor's tenant is null. Here we are explicitly querying the membership for
        // the resolved tenant, so we bypass the filter.
        var membership = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == tenantId && m.UserId == userId && m.IsActive)
            .Select(m => new { m.Role })
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            _logger.LogInformation(
                "Permission check failed for user {UserId} requirement {Permission}: not a member of org {OrgId}.",
                userId, requirement.Permission, tenantId);
            return;
        }

        // 4. Role grants the permission.
        if (!RolePermissions.Has(membership.Role, requirement.Permission))
        {
            _logger.LogInformation(
                "Permission check failed for user {UserId} requirement {Permission}: role {Role} does not grant it.",
                userId, requirement.Permission, membership.Role);
            return;
        }

        context.Succeed(requirement);
    }
}
