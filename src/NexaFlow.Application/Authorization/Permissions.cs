namespace NexaFlow.Application.Authorization;

/// <summary>
///     Permission constants. Each represents a coarse-grained capability.
///     Organization-level permissions are enforced via [Authorize(Policy = ...)].
///     Resource-level access (project membership, task ownership) is enforced
///     in handlers via ProjectAccess.LoadAndAuthorizeAsync.
/// </summary>
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

    // Projects
    public const string ProjectRead = "project.read";
    public const string ProjectCreate = "project.create";
    public const string ProjectUpdate = "project.update";
    public const string ProjectDelete = "project.delete";

    // Tasks
    public const string TaskRead = "task.read";
    public const string TaskCreate = "task.create";
    public const string TaskUpdate = "task.update";
    public const string TaskDelete = "task.delete";

    // Audit log (Phase 8)
    public const string AuditLogRead = "audit_log.read";
}
