namespace NexaFlow.Domain.Enums;

/// <summary>
///     Roles a user can hold within an organization. Used by the authorization layer
///     to evaluate permission-based access (section 9 — do NOT rely exclusively on roles;
///     permissions are the primary authorization primitive, roles group permissions).
/// </summary>
public enum OrganizationRole
{
    /// <summary>Sentinel value — no role assigned. Never stored on a real membership.</summary>
    None = 0,

    /// <summary>Full control of the organization. Exactly one Owner per organization.</summary>
    Owner = 1,

    /// <summary>Administrative privileges — manage members and settings, cannot remove Owner.</summary>
    Admin = 2,

    /// <summary>Standard member — can create / update projects and tasks.</summary>
    Member = 3,

    /// <summary>Read-only access — can view but not modify.</summary>
    Viewer = 4
}
