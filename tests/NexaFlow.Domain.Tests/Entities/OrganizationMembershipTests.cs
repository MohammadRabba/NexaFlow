using FluentAssertions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Phase 3 tests for the Organization aggregate's ownership and membership invariants.
///     These are DOMAIN tests — they assert on invariants, not on who can call what
///     (authorization is asserted on in Application-layer + integration tests).
/// </summary>
public sealed class OrganizationMembershipTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Organization CreateWithOwner(out Guid ownerId)
    {
        ownerId = Guid.NewGuid();
        return Organization.Create("Acme", "acme", ownerId, Now);
    }

    // --- AddMember ---

    [Fact]
    public void AddMember_adds_active_member_with_requested_role()
    {
        var org = CreateWithOwner(out var ownerId);
        var otherUserId = Guid.NewGuid();

        var member = org.AddMember(otherUserId, OrganizationRole.Admin, Now);

        member.UserId.Should().Be(otherUserId);
        member.Role.Should().Be(OrganizationRole.Admin);
        member.IsActive.Should().BeTrue();
        org.Members.Should().Contain(m => m.UserId == otherUserId);
    }

    [Fact]
    public void AddMember_rejects_Owner_role_assignment()
    {
        var org = CreateWithOwner(out _);
        var act = () => org.AddMember(Guid.NewGuid(), OrganizationRole.Owner, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMember_rejects_None_role()
    {
        var org = CreateWithOwner(out _);
        var act = () => org.AddMember(Guid.NewGuid(), OrganizationRole.None, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMember_rejects_duplicate_membership()
    {
        var org = CreateWithOwner(out var ownerId);
        org.AddMember(Guid.NewGuid(), OrganizationRole.Member, Now);

        var act = () => org.AddMember(ownerId, OrganizationRole.Member, Now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already a member*");
    }

    // --- RemoveMember ---

    [Fact]
    public void RemoveMember_removes_a_non_owner_member()
    {
        var org = CreateWithOwner(out _);
        var otherUserId = Guid.NewGuid();
        org.AddMember(otherUserId, OrganizationRole.Member, Now);

        org.RemoveMember(otherUserId, Now);

        org.Members.Should().NotContain(m => m.UserId == otherUserId);
    }

    [Fact]
    public void RemoveMember_rejects_removing_the_owner()
    {
        var org = CreateWithOwner(out var ownerId);
        org.AddMember(Guid.NewGuid(), OrganizationRole.Member, Now);

        var act = () => org.RemoveMember(ownerId, Now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot remove the Owner*");
    }

    [Fact]
    public void RemoveMember_rejects_removing_a_non_member()
    {
        var org = CreateWithOwner(out _);
        var act = () => org.RemoveMember(Guid.NewGuid(), Now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a member*");
    }

    // --- TransferOwnership ---

    [Fact]
    public void TransferOwnership_promotes_target_and_demotes_old_owner_to_Admin()
    {
        var org = CreateWithOwner(out var oldOwnerId);
        var newOwnerId = Guid.NewGuid();
        org.AddMember(newOwnerId, OrganizationRole.Member, Now);
        var actingUser = Guid.NewGuid();

        org.TransferOwnership(newOwnerId, actingUser, Now);

        org.OwnerUserId.Should().Be(newOwnerId);
        var newOwner = org.Members.Single(m => m.UserId == newOwnerId);
        newOwner.Role.Should().Be(OrganizationRole.Owner);
        var oldOwner = org.Members.Single(m => m.UserId == oldOwnerId);
        oldOwner.Role.Should().Be(OrganizationRole.Admin);
        org.UpdatedByUserId.Should().Be(actingUser);
    }

    [Fact]
    public void TransferOwnership_rejects_target_that_is_not_a_member()
    {
        var org = CreateWithOwner(out _);
        var act = () => org.TransferOwnership(Guid.NewGuid(), null, Now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a member*");
    }

    [Fact]
    public void TransferOwnership_rejects_transferring_to_self()
    {
        var org = CreateWithOwner(out var ownerId);
        var act = () => org.TransferOwnership(ownerId, null, Now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already the Owner*");
    }

    // --- ChangeRole on OrganizationMember ---

    [Fact]
    public void ChangeRole_rejects_changing_an_owners_role()
    {
        var member = OrganizationMember.CreateAsOwner(Guid.NewGuid(), Guid.NewGuid(), Now);
        var act = () => member.ChangeRole(OrganizationRole.Admin, Guid.NewGuid(), Now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot change an Owner's role*");
    }

    [Fact]
    public void ChangeRole_rejects_assigning_Owner_role()
    {
        var member = OrganizationMember.CreateAsMember(Guid.NewGuid(), Guid.NewGuid(), Now);
        var act = () => member.ChangeRole(OrganizationRole.Owner, Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();
    }

    // --- Invitation acceptance ---

    [Fact]
    public void AcceptInvitation_with_matching_hash_clears_token_and_activates()
    {
        var memberId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        const string hash = "token-hash-value";
        var invite = OrganizationMember.CreatePendingInvite(orgId, memberId, OrganizationRole.Member, hash, Now.AddDays(1), Now);

        var accepted = invite.AcceptInvitation(hash, Now);

        accepted.Should().BeTrue();
        invite.IsActive.Should().BeTrue();
        invite.InvitationTokenHash.Should().BeNull();
        invite.AcceptedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void AcceptInvitation_with_mismatched_hash_returns_false()
    {
        var invite = OrganizationMember.CreatePendingInvite(
            Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Member, "real-hash", Now.AddDays(1), Now);

        var accepted = invite.AcceptInvitation("wrong-hash", Now);

        accepted.Should().BeFalse();
        invite.IsActive.Should().BeFalse();
        invite.InvitationTokenHash.Should().Be("real-hash");
    }

    [Fact]
    public void CreatePendingInvite_rejects_Owner_role()
    {
        var act = () => OrganizationMember.CreatePendingInvite(
            Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Owner, "hash", Now.AddDays(1), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AcceptInvitation_with_expired_token_returns_false()
    {
        var memberId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        const string hash = "token-hash-value";
        // Token expired 1 hour ago.
        var invite = OrganizationMember.CreatePendingInvite(orgId, memberId, OrganizationRole.Member, hash, Now.AddHours(-1), Now.AddHours(-2));

        var accepted = invite.AcceptInvitation(hash, Now);

        accepted.Should().BeFalse();
        invite.IsActive.Should().BeFalse();
        invite.InvitationTokenHash.Should().Be(hash);
    }
}
