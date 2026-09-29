namespace NexaFlow.Domain.Enums;

/// <summary>
///     Lifecycle status of a project. State transitions are constrained by the
///     <see cref="ProjectStatusExtensions.CanTransitionTo" /> map — the domain
///     <c>Project.ChangeStatus</c> consults it.
/// </summary>
public enum ProjectStatus
{
    None = 0,
    Planning = 1,
    Active = 2,
    OnHold = 3,
    Completed = 4,
    Archived = 5
}

/// <summary>
///     Role a user holds within a specific project. Separate from
///     <see cref="OrganizationRole" /> — org-level and project-level access
///     are different authorization boundaries (a user can be Admin in the
///     org but Reader on a specific project).
/// </summary>
public enum ProjectMemberRole
{
    None = 0,
    /// <summary>Full project control: rename, change status, manage members, delete.</summary>
    Owner = 1,
    /// <summary>Operational: create/update tasks. Cannot manage project members or delete the project.</summary>
    Contributor = 2,
    /// <summary>Read-only access to the project.</summary>
    Reader = 3
}

/// <summary>
///     State-transition rules for <see cref="ProjectStatus" />. Kept as a small static
///     lookup so the valid transitions are visible in one place; the domain
///     <c>Project.ChangeStatus</c> enforces them.
/// </summary>
public static class ProjectStatusExtensions
{
    private static readonly Dictionary<ProjectStatus, HashSet<ProjectStatus>> AllowedTransitions = new()
    {
        [ProjectStatus.Planning] =
        [
            ProjectStatus.Active,
            ProjectStatus.Archived
        ],
        [ProjectStatus.Active] =
        [
            ProjectStatus.OnHold,
            ProjectStatus.Completed,
            ProjectStatus.Archived
        ],
        [ProjectStatus.OnHold] =
        [
            ProjectStatus.Active,
            ProjectStatus.Archived
        ],
        [ProjectStatus.Completed] =
        [
            ProjectStatus.Archived,
            ProjectStatus.Active // reopen if work resumes
        ],
        [ProjectStatus.Archived] =
        [
            ProjectStatus.Active // un-archive
        ],
        [ProjectStatus.None] = []
    };

    /// <summary>
    ///     True if a project in <paramref name="from" /> status can transition to
    ///     <paramref name="to" />. Same-value transitions are no-ops in the domain
    ///     (ChangeStatus returns early), so this method returns false for them — the
    ///     caller doesn't need to consult it for same-value cases.
    /// </summary>
    public static bool CanTransitionTo(this ProjectStatus from, ProjectStatus to)
    {
        if (from == to) return false;
        if (to == ProjectStatus.None) return false;
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}
