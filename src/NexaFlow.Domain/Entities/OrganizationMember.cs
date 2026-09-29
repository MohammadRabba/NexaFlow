using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A user's membership in an organization. Carries the <see cref="OrganizationRole" />
///     they hold. This entity is the source of truth for "what can user U do in org O" —
///     the JWT never carries role claims (per Phase 2 directive; ADR-005 documents this).
/// </summary>
/// <remarks>
///     Most mutations on a membership are private — they go through the
///     <see cref="Organization" /> aggregate root, which enforces cross-member
///     invariants (e.g., "exactly one Owner"). The methods exposed here are
///     single-member invariants only.
/// </remarks>
public class OrganizationMember : AuditableEntity, ITenantEntity
{
    private OrganizationMember() { }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Hashed invitation token, when the membership is pending acceptance.
    ///     Null when the membership is direct (Owner at org creation) or already accepted.
    ///     Plaintext never persisted (per Phase 2 directive; ADR-005 §1.5 pattern).
    /// </summary>
    public string? InvitationTokenHash { get; private set; }

    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    // --- Factories ---

    public static OrganizationMember CreateAsOwner(Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(organizationId, userId, OrganizationRole.Owner, isActive: true, atUtc);

    public static OrganizationMember CreateAsAdmin(Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(organizationId, userId, OrganizationRole.Admin, isActive: true, atUtc);

    public static OrganizationMember CreateAsMember(Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(organizationId, userId, OrganizationRole.Member, isActive: true, atUtc);

    public static OrganizationMember CreateAsViewer(Guid organizationId, Guid userId, DateTimeOffset atUtc)
        => CreateInternal(organizationId, userId, OrganizationRole.Viewer, isActive: true, atUtc);

    /// <summary>
    ///     Factory for an invited-but-not-yet-accepted membership. The invitation token
    ///     hash is stored; the plaintext is returned by the caller (who sends it via email).
    /// </summary>
    public static OrganizationMember CreatePendingInvite(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        string invitationTokenHash,
        DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationTokenHash);
        if (role is OrganizationRole.Owner or OrganizationRole.None)
            throw new ArgumentException("Invitations cannot assign Owner or None.", nameof(role));

        var member = CreateInternal(organizationId, userId, role, isActive: false, atUtc);
        member.InvitationTokenHash = invitationTokenHash;
        return member;
    }

    // NOTE: `internal` because Organization aggregate calls this from its own factories.
    //       We don't want Application code or handlers to bypass Organization.AddMember.
    internal static OrganizationMember CreateInternal(
        Guid organizationId, Guid userId, OrganizationRole role, bool isActive, DateTimeOffset atUtc)
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

    // --- Single-member mutations (cross-member invariants are enforced by Organization) ---

    /// <summary>
    ///     Change the member's role. Refuses to touch an Owner — ownership must be
    ///     transferred via <see cref="Organization.TransferOwnership" />.
    /// </summary>
    public void ChangeRole(OrganizationRole newRole, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (Role == OrganizationRole.Owner)
            throw new InvalidOperationException(
                "Cannot change an Owner's role. Transfer ownership instead.");
        if (newRole is OrganizationRole.None or OrganizationRole.Owner)
            throw new ArgumentException(
                "ChangeRole cannot assign Owner or None. Use TransferOwnership to change ownership.",
                nameof(newRole));
        if (Role == newRole) return;
        Role = newRole;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    /// <summary>
    ///     Accept a pending invitation. Clears the token hash (single-use) and marks active.
    /// </summary>
    public bool AcceptInvitation(string presentedTokenHash, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedTokenHash);
        if (IsActive) return true; // already accepted — idempotent
        if (InvitationTokenHash is null) return false;
        if (!string.Equals(InvitationTokenHash, presentedTokenHash, StringComparison.Ordinal))
            return false;

        IsActive = true;
        AcceptedAtUtc = atUtc;
        InvitationTokenHash = null;
        UpdatedAtUtc = atUtc;
        return true;
    }

    /// <summary>
    ///     Deactivate the membership (e.g., the user leaves the organization voluntarily).
    ///     Currently unused — Phase 3 uses hard-delete via <see cref="Organization.RemoveMember" />.
    ///     Kept here for the soft-delete variant if we need it later (e.g., audit-trail preservation).
    /// </summary>
    public void Deactivate(Guid? byUserId, DateTimeOffset atUtc)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = byUserId;
    }

    // --- Internal methods called by Organization aggregate during ownership transfer ---

    internal void PromoteToOwner(DateTimeOffset atUtc)
    {
        Role = OrganizationRole.Owner;
        UpdatedAtUtc = atUtc;
    }

    internal void DemoteFromOwner(DateTimeOffset atUtc)
    {
        Role = OrganizationRole.Admin;
        UpdatedAtUtc = atUtc;
    }
}
