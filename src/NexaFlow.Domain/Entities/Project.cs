using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A project within an organization. Tenant-owned via <see cref="OrganizationId" />.
/// </summary>
/// <remarks>
///     <para>Business invariants enforced here:</para>
///     <list type="bullet">
///         <item>A project has exactly one Owner at any time.</item>
///         <item>The Owner can only change via <see cref="TransferOwnership" />.</item>
///         <item>A member cannot remove the Owner; ownership must be transferred first.</item>
///         <item>Duplicate project memberships are rejected at the aggregate boundary.</item>
///         <item>Status transitions are validated against the state-transition map
///         (see <see cref="ProjectStatusExtensions.CanTransitionTo" />); invalid
///         transitions throw <see cref="InvalidStateTransitionException" />.</item>
///         <item>DueDate, if set, must be on or after StartDate (when both are set).</item>
///     </list>
///     <para>Authorization rules (who can call these methods) live in the Application
///     handlers, not here.</para>
/// </remarks>
public class Project : AggregateRoot, ITenantEntity
{
    private readonly List<ProjectMember> _members = [];

    private Project() { }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ProjectStatus Status { get; private set; } = ProjectStatus.Planning;

    public DateTimeOffset? StartDateUtc { get; private set; }
    public DateTimeOffset? DueDateUtc { get; private set; }

    /// <summary>The user id of the current project Owner. Exactly one at any time.</summary>
    public Guid OwnerUserId { get; private set; }

    public IReadOnlyList<ProjectMember> Members => _members.AsReadOnly();

    public static Project Create(
        Guid organizationId,
        string name,
        string description,
        Guid ownerUserId,
        DateTimeOffset atUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 100) throw new ArgumentException("Project name must not exceed 100 characters.");
        if (description.Length > 2000) throw new ArgumentException("Description must not exceed 2000 characters.");
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException("OwnerUserId must not be empty.", nameof(ownerUserId));

        var project = new Project
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = ProjectStatus.Planning,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };

        var ownerMember = ProjectMember.CreateAsOwner(project.Id, organizationId, ownerUserId, atUtc);
        project._members.Add(ownerMember);

        return project;
    }

    public void Rename(string newName, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.Length > 100) throw new ArgumentException("Project name must not exceed 100 characters.");
        if (string.Equals(Name, newName, StringComparison.Ordinal)) return;
        Name = newName.Trim();
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    public void UpdateDescription(string newDescription, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (newDescription is null)
        {
            if (Description.Length == 0) return;
            Description = string.Empty;
        }
        else
        {
            if (newDescription.Length > 2000) throw new ArgumentException("Description must not exceed 2000 characters.");
            if (string.Equals(Description, newDescription, StringComparison.Ordinal)) return;
            Description = newDescription.Trim();
        }
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    /// <summary>
    ///     Set the start and due dates. Both optional. If both are set, DueDate must be
    ///     on or after StartDate.
    /// </summary>
    public void SetDates(
        DateTimeOffset? startDateUtc,
        DateTimeOffset? dueDateUtc,
        Guid? updatedByUserId,
        DateTimeOffset atUtc)
    {
        if (startDateUtc.HasValue && dueDateUtc.HasValue && dueDateUtc < startDateUtc)
            throw new DomainException("Due date must be on or after start date.", "INVALID_PROJECT_DATES");

        StartDateUtc = startDateUtc;
        DueDateUtc = dueDateUtc;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    /// <summary>
    ///     Transition the project to a new status. Validated against the state-transition map.
    ///     Throws <see cref="InvalidStateTransitionException" /> on invalid transitions.
    /// </summary>
    public void ChangeStatus(ProjectStatus target, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (target == ProjectStatus.None)
            throw new ArgumentException("Target status must not be None.", nameof(target));
        if (Status == target) return;

        if (!Status.CanTransitionTo(target))
            throw new InvalidStateTransitionException(nameof(Project), Status.ToString(), target.ToString());

        Status = target;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    /// <summary>
    ///     Add a member. Rejects duplicates and Owner role (use <see cref="TransferOwnership" />).
    /// </summary>
    public ProjectMember AddMember(Guid userId, ProjectMemberRole role, DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (role is ProjectMemberRole.None or ProjectMemberRole.Owner)
            throw new ArgumentException(
                "AddMember does not assign Owner. Use TransferOwnership to change ownership.",
                nameof(role));

        if (_members.Any(m => m.UserId == userId))
            throw new InvalidOperationException(
                $"User {userId} is already a member of this project.");

        var member = ProjectMember.CreateInternal(Id, OrganizationId, userId, role, atUtc);
        _members.Add(member);
        UpdatedAtUtc = atUtc;
        return member;
    }

    /// <summary>
    ///     Remove a member. Refuses the Owner — must transfer first.
    /// </summary>
    public void RemoveMember(Guid userId, DateTimeOffset atUtc)
    {
        if (userId == OwnerUserId)
            throw new InvalidOperationException("Cannot remove the project Owner. Transfer ownership first.");

        var member = _members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new InvalidOperationException(
                $"User {userId} is not a member of this project.");

        _members.Remove(member);
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    ///     Transfer ownership to an existing member. The previous Owner becomes a Contributor.
    /// </summary>
    public void TransferOwnership(Guid toUserId, Guid? byUserId, DateTimeOffset atUtc)
    {
        if (toUserId == Guid.Empty)
            throw new ArgumentException("Target user id must not be empty.", nameof(toUserId));
        if (toUserId == OwnerUserId)
            throw new InvalidOperationException("User is already the project Owner.");

        var target = _members.FirstOrDefault(m => m.UserId == toUserId)
            ?? throw new InvalidOperationException(
                $"User {toUserId} is not a member of this project. Add them as a member before transferring ownership.");

        var currentOwner = _members.Single(m => m.UserId == OwnerUserId);
        target.PromoteToOwner(atUtc);
        currentOwner.DemoteFromOwner(atUtc);
        OwnerUserId = toUserId;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = byUserId;
    }
}
