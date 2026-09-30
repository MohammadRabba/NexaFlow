using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Authorization;

/// <summary>
///     Centralized role → permission mapping. The single source of truth for
///     "what can role R do?". Per the Phase 3 directive, role names should NOT
///     appear as <c>if (role == Owner)</c> scattered through handlers — all such
///     questions go through this class.
/// </summary>
/// <remarks>
///     <para>
///         This mapping is the business rule, not an abstraction. It is read by the
///         permission-requirement authorization handlers in the API layer (which
///         translate "user has role R, needs permission P" into ASP.NET Core
///         authorization decisions).
///     </para>
///     <para>
///         Mapping rationale (per role):
///     </para>
///     <list type="table">
///         <list>
///             <header><term>Owner</term><description>Full administrative access including ownership transfer and deletion. The Owner is the single point of accountability for the organization.</description></header>
///         </list>
///         <list>
///             <header><term>Admin</term><description>Day-to-day administration: invite, role changes (Member/Viewer only — Admin cannot touch another Admin), remove non-Owner members. Cannot transfer ownership or delete the organization.</description></header>
///         </list>
///         <list>
///             <header><term>Member</term><description>Operational access: create / read / update Projects and Tasks. Cannot manage members or organizations.</description></header>
///         </list>
///         <list>
///             <header><term>Viewer</term><description>Read-only across the tenant. No mutations.</description></header>
///         </list>
///     </list>
/// </remarks>
public static class RolePermissions
{
    /// <summary>
    ///     Returns the set of permission strings granted to <paramref name="role" />.
    ///     Returns an empty set for unknown / None roles.
    /// </summary>
    public static IReadOnlySet<string> For(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => OwnerPermissions,
        OrganizationRole.Admin => AdminPermissions,
        OrganizationRole.Member => MemberPermissions,
        OrganizationRole.Viewer => ViewerPermissions,
        _ => EmptySet
    };

    /// <summary>
    ///     True if <paramref name="role" /> grants <paramref name="permission" />.
    ///     Equivalent to <c>For(role).Contains(permission)</c> but more readable at call sites.
    /// </summary>
    public static bool Has(OrganizationRole role, string permission)
        => For(role).Contains(permission);

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    private static readonly IReadOnlySet<string> OwnerPermissions = new HashSet<string>
    {
        Permissions.OrganizationRead,
        Permissions.OrganizationUpdate,
        Permissions.OrganizationDelete,
        Permissions.MemberRead,
        Permissions.MemberInvite,
        Permissions.MemberUpdate,
        Permissions.MemberRemove,
        Permissions.MemberTransferOwnership,
        Permissions.ProjectRead,
        Permissions.ProjectCreate,
        Permissions.ProjectUpdate,
        Permissions.ProjectDelete,
        Permissions.TaskRead,
        Permissions.TaskCreate,
        Permissions.TaskUpdate,
        Permissions.TaskDelete,
        Permissions.AuditLogRead,
    };

    private static readonly IReadOnlySet<string> AdminPermissions = new HashSet<string>
    {
        Permissions.OrganizationRead,
        Permissions.MemberRead,
        Permissions.MemberInvite,
        Permissions.MemberUpdate,
        Permissions.MemberRemove,
        Permissions.ProjectRead,
        Permissions.ProjectCreate,
        Permissions.ProjectUpdate,
        Permissions.ProjectDelete,
        Permissions.TaskRead,
        Permissions.TaskCreate,
        Permissions.TaskUpdate,
        Permissions.TaskDelete,
        Permissions.AuditLogRead,
    };

    private static readonly IReadOnlySet<string> MemberPermissions = new HashSet<string>
    {
        Permissions.OrganizationRead,
        Permissions.MemberRead,
        Permissions.ProjectRead,
        Permissions.ProjectCreate,
        Permissions.ProjectUpdate,
        Permissions.TaskRead,
        Permissions.TaskCreate,
        Permissions.TaskUpdate,
    };

    private static readonly IReadOnlySet<string> ViewerPermissions = new HashSet<string>
    {
        Permissions.OrganizationRead,
        Permissions.MemberRead,
        Permissions.ProjectRead,
        Permissions.TaskRead,
    };
}
