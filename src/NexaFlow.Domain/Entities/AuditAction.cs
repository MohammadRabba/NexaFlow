namespace NexaFlow.Domain.Entities;

/// <summary>
///     Canonical audit action names (spec §25 — examples list). Centralized so that
///     <see cref="AuditLog.Action" /> values are stable strings, not magic literals
///     scattered across handlers. Adding a new audited operation = adding a constant
///     here + recording it from the relevant handler.
/// </summary>
/// <remarks>
///     <para>
///         Section 25 examples: TaskCreated, TaskUpdated, TaskDeleted, TaskStatusChanged,
///         MemberInvited, MemberRemoved, RoleChanged, LoginSucceeded, LoginFailed,
///         PasswordChanged.
///     </para>
///     <para>
///         The set below covers those plus a small number of additional operations that
///         are explicitly listed elsewhere in the spec as auditable (project / organization /
///         comment mutations, project-member mutations, organization ownership transfer,
///         email verification, refresh-token rotation, logout). No speculative actions
///         are added — every constant maps to a real handler call site.
///     </para>
/// </remarks>
public static class AuditAction
{
    // --- Authentication events (no tenant context; OrganizationId will be null) ---
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string PasswordChanged = "PasswordChanged";
    public const string EmailVerified = "EmailVerified";
    public const string RefreshTokenRotated = "RefreshTokenRotated";

    // --- Organization-scoped operations ---
    public const string OrganizationCreated = "OrganizationCreated";
    public const string OrganizationUpdated = "OrganizationUpdated";
    public const string OrganizationDeleted = "OrganizationDeleted";

    // --- Member (organization-level) changes ---
    public const string MemberInvited = "MemberInvited";
    public const string MemberRemoved = "MemberRemoved";
    public const string RoleChanged = "RoleChanged";
    public const string OwnershipTransferred = "OwnershipTransferred";

    // --- Project lifecycle ---
    public const string ProjectCreated = "ProjectCreated";
    public const string ProjectUpdated = "ProjectUpdated";
    public const string ProjectDeleted = "ProjectDeleted";

    // --- Project-member changes ---
    public const string ProjectMemberAdded = "ProjectMemberAdded";
    public const string ProjectMemberRemoved = "ProjectMemberRemoved";
    public const string ProjectMemberRoleChanged = "ProjectMemberRoleChanged";
    public const string ProjectOwnershipTransferred = "ProjectOwnershipTransferred";

    // --- Task lifecycle (incl. status / priority / assignment changes) ---
    public const string TaskCreated = "TaskCreated";
    public const string TaskUpdated = "TaskUpdated";
    public const string TaskDeleted = "TaskDeleted";
    public const string TaskStatusChanged = "TaskStatusChanged";

    // --- Comments ---
    public const string CommentCreated = "CommentCreated";
    public const string CommentUpdated = "CommentUpdated";
    public const string CommentDeleted = "CommentDeleted";

    // --- Labels ---
    public const string LabelCreated = "LabelCreated";
    public const string LabelDeleted = "LabelDeleted";
}
