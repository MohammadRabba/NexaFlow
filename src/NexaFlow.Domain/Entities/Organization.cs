using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Events.Organizations;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     An organization — the tenant root. All tenant-owned rows reference this
///     aggregate via <c>OrganizationId</c> (section 6).
///     <para>
///         The owner membership is created inline by the factory method to keep the
///         aggregate consistent (an organization without an owner is an invalid state).
///     </para>
///     <para>
///         The <c>slug</c> is URL-friendly and unique. Generated from the name and
///         validated for uniqueness at the Application layer (Phase 3).
///     </para>
/// </summary>
public class Organization : AggregateRoot
{
    private readonly List<OrganizationMember> _members = [];

    private Organization() { }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;

    /// <summary>The user id of the Owner role member. Exactly one owner at any time.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>Read-only view of the membership collection.</summary>
    public IReadOnlyList<OrganizationMember> Members => _members.AsReadOnly();

    /// <summary>
    ///     Factory for a new organization. Creates the Owner membership as part of the
    ///     same aggregate transaction and raises <see cref="OrganizationCreatedEvent" />.
    /// </summary>
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

        // Owner membership is part of the aggregate
        var ownerMember = OrganizationMember.CreateAsOwner(org.Id, ownerUserId, createdAtUtc);
        org._members.Add(ownerMember);

        org.AddDomainEvent(new OrganizationCreatedEvent(org.Id, ownerUserId, org.Name, createdAtUtc));
        return org;
    }

    /// <summary>Rename the organization. Idempotent if the new name is identical.</summary>
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
}
