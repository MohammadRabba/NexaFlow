using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Projects.Commands;

/// <summary>
///     Shared access checks for project handlers. Each method is small and explicit — no
///     generic framework, no strategy pattern, just the two checks Phase 4 actually needs:
///     "the project's tenant matches the resolved tenant" (cross-tenant guard) and
///     "the current user is a member of this project with at least the given role"
///     (resource-level authorization).
/// </summary>
/// <remarks>
///     This is NOT a base class. Project handlers receive it via constructor injection
///     and call the methods at the start of <c>Handle</c>. The "ProjectAccess" object
///     itself is stateless — a thin function-bag. Putting these checks in a shared class
///     rather than copying them across handlers is justified because we have 5 project
///     mutation handlers and the check sequence is identical. (The Phase 4 directive
///     warns against premature abstractions; this isn't one — it's just shared code
///     for a real recurring pattern.)
/// </remarks>
public sealed class ProjectAccess
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public ProjectAccess(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    /// <summary>
    ///     Load the project with its members, verify the tenant matches the resolved
    ///     tenant, verify the current user is a member of this project with at least the
    ///     <paramref name="minimumRole" /> role. Returns the loaded project for the handler
    ///     to mutate. Throws NotFoundException (→404) on any failure — never reveals whether
    ///     the project exists in another tenant or the user lacks access (same response).
    /// </summary>
    public async Task<Project> LoadAndAuthorizeAsync(
        Guid projectId,
        ProjectMemberRole minimumRole,
        CancellationToken cancellationToken)
    {
        // Authenticated user — controller's [Authorize] already checked, defense in depth.
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");

        // Tenant must be resolved.
        var resolvedTenantId = _currentTenant.RequireTenantId();

        var project = await _db.FindProjectWithMembersAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("Project", projectId);

        // Cross-tenant guard: the loaded project's OrganizationId MUST equal the resolved
        // tenant. If not, 404 — same response as "not found".
        if (project.OrganizationId != resolvedTenantId)
            throw new NotFoundException("Project", projectId);

        // Resource-level authorization: the user must be an active member of THIS project
        // (not just the org) with at least the minimum role.
        var membership = project.Members.FirstOrDefault(m => m.UserId == userId);
        if (membership is null)
            throw new NotFoundException("Project", projectId);

        if (!RoleGrants(membership.Role, minimumRole))
            throw new NotFoundException("Project", projectId);  // 404 to avoid enumeration

        return project;
    }

    /// <summary>
    ///     Returns true if <paramref name="held" /> satisfies <paramref name="required" />.
    /// ProjectMemberRole has a strict hierarchy: Owner &gt; Contributor &gt; Reader.
    /// </summary>
    private static bool RoleGrants(ProjectMemberRole held, ProjectMemberRole required)
        => held switch
        {
            ProjectMemberRole.Owner => true,
            ProjectMemberRole.Contributor => required is ProjectMemberRole.Contributor or ProjectMemberRole.Reader,
            ProjectMemberRole.Reader => required == ProjectMemberRole.Reader,
            _ => false
        };
}
