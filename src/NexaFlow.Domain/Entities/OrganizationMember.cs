using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     The membership link between a <see cref="User" /> and an <see cref="Organization" />,
///     with the <see cref="OrganizationRole" /> the user holds in that organization.
///     <para>
///         This is the entity the authorization layer queries to answer:
///         "Is user U a member of org O? What role do they hold?"
///         The (OrganizationId, UserId) pair is unique (Phase 1 EF configuration).
///     </para>
///     <para>
///         This entity is itself tenant-scoped — it carries an OrganizationId and implements
///         <see cref="ITenantEntity" /> so the global query filter applies to it too.
///     </para>
/// </summary>
public class OrganizationMember : AuditableEntity, ITenantEntity
{
    private OrganizationMember() { }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }

    /// <summary>
    ///     Invitation acceptance state. Phase 3 will implement the invite flow; for Phase 1
    ///     a member is considered active immediately (used for the Owner membership at org creation).
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     If invited but not yet accepted, this holds the hashed invitation token (Phase 3).
    ///     Null when the membership is direct or already accepted.
    /// </summary>
    public string? InvitationTokenHash { get; private set; }

    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public static OrganizationMember CreateAsOwner(Guid organizationId, Guid userId, DateTimeOffset atUtc)
    {
        return CreateInternal(organizationId, userId, OrganizationRole.Owner, isActive: true, atUtc);
    }

    public static OrganizationMember CreateAsAdmin(Guid organizationId, Guid userId, DateTimeOffset atUtc)
    {
        return CreateInternal(organizationId, userId, OrganizationRole.Admin, isActive: true, atUtc);
    }

    public static OrganizationMember CreateAsMember(Guid organizationId, Guid userId, DateTimeOffset atUtc)
    {
        return CreateInternal(organizationId, userId, OrganizationRole.Member, isActive: true, atUtc);
    }

    private static OrganizationMember CreateInternal(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        bool isActive,
        DateTimeOffset atUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));

        return new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            IsActive = isActive,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    /// <summary>Change the user's role within this organization.</summary>
    public void ChangeRole(OrganizationRole newRole, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (Role == OrganizationRole.Owner)
            throw new InvalidOperationException(
                "Cannot change an Owner's role. Ownership must be transferred explicitly.");
        if (Role == newRole) return;
        Role = newRole;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }
}
