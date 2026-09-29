using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Events.Organizations;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     An organization — the tenant root. All tenant-owned rows reference this
///     aggregate via <c>OrganizationId</c>.
/// </summary>
/// <remarks>
///     Business invariants enforced here:
///     <list type="bullet">
///         <item>An organization has exactly one Owner at any time.</item>
///         <item>The Owner can only change via <see cref="TransferOwnership" />, never by directly mutating a member's role.</item>
///         <item>A member cannot remove the Owner; ownership must be transferred first.</item>
///         <item>The last Owner cannot leave (no member to transfer to).</item>
///         <item>Duplicate memberships are rejected at the aggregate boundary before hitting the unique constraint.</item>
///     </list>
///     Authorization rules (who can call these methods) are NOT enforced here —
///     they live in the authorization layer in Application.
/// </remarks>
public class Organization : AggregateRoot
{
    private readonly List<OrganizationMember> _members = [];

    private Organization() { }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;

    /// <summary>
    ///     The user id of the current Owner. Exactly one Owner exists at any time.
    ///     Denormalized from <see cref="_members" /> for fast lookup; updated on
    ///     <see cref="TransferOwnership" />.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    public IReadOnlyList<OrganizationMember> Members => _members.AsReadOnly();

    public static Organization Create(string name, string slug, Guid ownerUserId, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (name.Length is < 1 or > 100)
            throw new ArgumentException("Organization name must be 1..100 characters.");
        if (slug.Length is < 2 or > 60)
            throw new ArgumentException("Organization slug must be 2..60 characters.");

        var org = new Organization
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            OwnerUserId = ownerUserId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        var ownerMember = OrganizationMember.CreateAsOwner(org.Id, ownerUserId, createdAtUtc);
        org._members.Add(ownerMember);

        org.AddDomainEvent(new OrganizationCreatedEvent(org.Id, ownerUserId, org.Name, createdAtUtc));
        return org;
    }

    public void Rename(string newName, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.Length is < 1 or > 100)
            throw new ArgumentException("Organization name must be 1..100 characters.");
        if (string.Equals(Name, newName, StringComparison.Ordinal)) return;
        Name = newName.Trim();
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    /// <summary>
    ///     Add a member to this organization. Duplicate memberships (same user id) are
    ///     rejected at the aggregate boundary, before the database unique constraint
    ///     would fire. This is also a fail-fast against races where two admins invite
    ///     the same user simultaneously.
    /// </summary>
    public OrganizationMember AddMember(Guid userId, OrganizationRole role, DateTimeOffset atUtc)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (role is OrganizationRole.None or OrganizationRole.Owner)
            throw new ArgumentException(
                "AddMember does not assign Owner. Use TransferOwnership to change ownership.",
                nameof(role));

        if (_members.Any(m => m.UserId == userId))
            throw new InvalidOperationException(
                $"User {userId} is already a member of this organization.");

        var member = OrganizationMember.CreateInternal(Id, userId, role, isActive: true, atUtc);
        _members.Add(member);
        UpdatedAtUtc = atUtc;
        return member;
    }

    /// <summary>
    ///     Remove a member. Refuses to remove the current Owner — the Owner must
    ///     transfer ownership first (so the invariant "exactly one Owner" is never
    ///     violated, even transiently, inside this method).
    ///     <para>
    ///         The membership entity is hard-deleted from the DbContext by the Application
    ///         handler (via <c>IApplicationDbContext.Remove</c>). We hard-delete because
    ///         audit log rows reference <c>user_id</c> + <c>organization_id</c>, not the
    ///         membership row itself, so we keep the audit trail without keeping a
    ///         dangling membership.
    ///     </para>
    /// </summary>
    public void RemoveMember(Guid userId, DateTimeOffset atUtc)
    {
        if (userId == OwnerUserId)
            throw new InvalidOperationException(
                "Cannot remove the Owner. Transfer ownership first.");

        var member = _members.FirstOrDefault(m => m.UserId == userId);
        if (member is null)
            throw new InvalidOperationException(
                $"User {userId} is not a member of this organization.");

        _members.Remove(member);
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    ///     Transfer ownership from the current Owner to <paramref name="toUserId" />,
    ///     who must already be an active member. The previous Owner becomes an Admin
    ///     (a courtesy role — they keep administrative access but no longer have the
    ///     "exactly one Owner" privilege).
    /// </summary>
    public void TransferOwnership(Guid toUserId, Guid? byUserId, DateTimeOffset atUtc)
    {
        if (toUserId == Guid.Empty)
            throw new ArgumentException("Target user id must not be empty.", nameof(toUserId));
        if (toUserId == OwnerUserId)
            throw new InvalidOperationException("User is already the Owner.");

        var target = _members.FirstOrDefault(m => m.UserId == toUserId)
            ?? throw new InvalidOperationException(
                $"User {toUserId} is not a member of this organization. " +
                "Add them as a member before transferring ownership.");

        var currentOwner = _members.Single(m => m.UserId == OwnerUserId);

        target.PromoteToOwner(atUtc);
        currentOwner.DemoteFromOwner(atUtc);

        OwnerUserId = toUserId;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = byUserId;
    }
}
