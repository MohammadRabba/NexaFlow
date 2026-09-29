using FluentAssertions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Events.Organizations;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Tests for the <see cref="Organization" /> aggregate — section 17 (Domain
///     Business Rules) and section 10 (avoid public setters). The aggregate's
///     factory method must:
///     <list type="bullet">
///         <item>Create an Owner membership as part of the same aggregate transaction.</item>
///         <item>Raise <see cref="OrganizationCreatedEvent" /> so Phase 6 can publish it via the outbox.</item>
///         <item>Reject empty Guids and out-of-bounds name/slug lengths.</item>
///     </list>
/// </summary>
public sealed class OrganizationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_should_create_owner_membership_and_raise_event()
    {
        // Arrange
        var ownerId = Guid.NewGuid();

        // Act
        var org = Organization.Create(
            name: "Acme Inc.",
            slug: "acme-inc",
            ownerUserId: ownerId,
            createdAtUtc: CreatedAt);

        // Assert
        org.Name.Should().Be("Acme Inc.");
        org.Slug.Should().Be("acme-inc"); // lowercase normalization
        org.OwnerUserId.Should().Be(ownerId);
        org.CreatedAtUtc.Should().Be(CreatedAt);
        org.UpdatedAtUtc.Should().Be(CreatedAt);

        // Aggregate invariant: an organization has at least one Owner membership
        org.Members.Should().ContainSingle();
        var owner = org.Members[0];
        owner.UserId.Should().Be(ownerId);
        owner.Role.Should().Be(OrganizationRole.Owner);
        owner.OrganizationId.Should().Be(org.Id);
        owner.IsActive.Should().BeTrue();

        // Event must be queued for dispatch (Phase 6 will consume it)
        org.DomainEvents.Should().ContainSingle();
        var evt = org.DomainEvents.OfType<OrganizationCreatedEvent>().Single();
        evt.OrganizationId.Should().Be(org.Id);
        evt.OwnerUserId.Should().Be(ownerId);
        evt.OccurredOnUtc.Should().Be(CreatedAt);
    }

    [Theory]
    [InlineData("", "acme")]                       // empty name
    [InlineData(null, "acme")]                       // null name
    [InlineData("Acme", "")]                         // empty slug
    [InlineData("Acme", "this-slug-is-far-too-long-for-the-sixty-character-limit-imposed-by-the-domain")] // slug too long
    public void Create_should_reject_invalid_inputs(string name, string slug)
    {
        var act = () => Organization.Create(name, slug, Guid.NewGuid(), CreatedAt);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_should_reject_empty_owner_id()
    {
        var act = () => Organization.Create("Acme", "acme", Guid.Empty, CreatedAt);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_should_update_name_and_audit_metadata()
    {
        // Arrange
        var org = Organization.Create("Old Name", "old-name", Guid.NewGuid(), CreatedAt);
        var updater = Guid.NewGuid();
        var updatedAt = CreatedAt.AddHours(1);

        // Act
        org.Rename("New Name", updater, updatedAt);

        // Assert
        org.Name.Should().Be("New Name");
        org.UpdatedAtUtc.Should().Be(updatedAt);
        org.UpdatedByUserId.Should().Be(updater);
    }

    [Fact]
    public void Rename_should_be_idempotent_for_same_name()
    {
        // Arrange
        var org = Organization.Create("Acme", "acme", Guid.NewGuid(), CreatedAt);
        var originalUpdatedAt = org.UpdatedAtUtc;

        // Act
        org.Rename("Acme", null, CreatedAt.AddHours(1));

        // Assert — nothing changes
        org.Name.Should().Be("Acme");
        org.UpdatedAtUtc.Should().Be(originalUpdatedAt);
    }
}
