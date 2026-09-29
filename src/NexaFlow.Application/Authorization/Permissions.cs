namespace NexaFlow.Application.Authorization;

/// <summary>
///     Permission constants. Each represents a coarse-grained capability. Resources
///     are scoped by tenant + ownership at runtime (see <see cref="ResourceAuthorizationHandler{T}" />);
///     a permission being granted does NOT automatically mean the user can act on every
///     resource of that type — the resource must also belong to the user's resolved tenant.
/// </summary>
/// <remarks>
///     Why const strings instead of an enum:
///     - Permissions are claims in the ASP.NET Core authorization system (which is string-based).
///     - Adding permissions incrementally doesn't require recompiling consumers that compare by string.
///     - No artificial enum-members-per-row count to maintain.
/// </remarks>
public static class Permissions
{
    // Organization
    public const string OrganizationRead = "organization.read";
    public const string OrganizationUpdate = "organization.update";
    public const string OrganizationDelete = "organization.delete";

    // Membership
    public const string MemberRead = "member.read";
    public const string MemberInvite = "member.invite";
    public const string MemberUpdate = "member.update";
    public const string MemberRemove = "member.remove";
    public const string MemberTransferOwnership = "member.transfer_ownership";

    // Projects (Phase 4; defined now so the authorization model is stable, but not enforced until Phase 4)
    public const string ProjectRead = "project.read";
    public const string ProjectCreate = "project.create";
    public const string ProjectUpdate = "project.update";
    public const string ProjectDelete = "project.delete";

    // Tasks (Phase 5; same rationale as Projects)
    public const string TaskRead = "task.read";
    public const string TaskCreate = "task.create";
    public const string TaskUpdate = "task.update";
    public const string TaskDelete = "task.delete";
}
