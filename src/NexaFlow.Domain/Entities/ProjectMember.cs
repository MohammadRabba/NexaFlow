using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A user's membership in a project. Carries a <see cref="ProjectMemberRole" />.
/// Tenant-scoped via the parent <see cref="Project" /> (which implements
/// <see cref="ITenantEntity" />). The (project_id, user_id) pair is unique —
/// a user has exactly one role per project.
/// </summary>
/// <remarks>
///     Single-member mutations are public; cross-member invariants (e.g.,
/// "exactly one project Owner") are enforced on the <see cref="Project" />
/// aggregate root.
/// </remarks>
public class ProjectMember : AuditableEntity, ITenantEntity
{
    private ProjectMember() { }

    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public ProjectMemberRole Role { get; private set; }

    // ITenantEntity.OrganizationId — derived from the parent Project. EF Core
    // configures this as a shadow property; for in-memory tests it's set explicitly
    // via the factory. The OrganizationId is ALWAYS the project's tenant.
    public Guid OrganizationId { get; private set; }

    public static ProjectMember CreateAsOwner(Guid projectId, Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(projectId, organizationId, userId, ProjectMemberRole.Owner, atUtc);

    public static ProjectMember CreateAsContributor(Guid projectId, Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(projectId, organizationId, userId, ProjectMemberRole.Contributor, atUtc);

    public static ProjectMember CreateAsReader(Guid projectId, Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(projectId, organizationId, userId, ProjectMemberRole.Reader, atUtc);

    // NOTE: `internal` so that the Project aggregate's AddMember calls this — Application code
    // bypasses the aggregate at its own risk (would lose the cross-member invariants).
    internal static ProjectMember CreateInternal(
        Guid projectId, Guid organizationId, Guid userId, ProjectMemberRole role, DateTimeOffset atUtc)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));

        return new ProjectMember
        {
            ProjectId = projectId,
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    /// <summary>
    ///     Change the role. Refuses Owner — ownership must go via <see cref="Project.TransferOwnership" />.
    /// </summary>
    public void ChangeRole(ProjectMemberRole newRole, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (Role == ProjectMemberRole.Owner)
            throw new InvalidOperationException(
                "Cannot change a project Owner's role. Transfer ownership instead.");
        if (newRole is ProjectMemberRole.None or ProjectMemberRole.Owner)
            throw new ArgumentException(
                "ChangeRole cannot assign Owner or None. Use TransferOwnership to change ownership.",
                nameof(newRole));
        if (Role == newRole) return;
        Role = newRole;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    // Internal — called by Project aggregate during ownership transfer.
    internal void PromoteToOwner(DateTimeOffset atUtc)
    {
        Role = ProjectMemberRole.Owner;
        UpdatedAtUtc = atUtc;
    }

    internal void DemoteFromOwner(DateTimeOffset atUtc)
    {
        Role = ProjectMemberRole.Contributor;
        UpdatedAtUtc = atUtc;
    }
}
