using FluentAssertions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Domain tests for the <see cref="Project" /> aggregate — invariants for creation,
/// status transitions, dates, and the membership/ownership rules that mirror (but are
/// distinct from) the Organization aggregate.
/// </summary>
public sealed class ProjectAggregateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Project CreateWithOwner(out Guid ownerId)
    {
        ownerId = Guid.NewGuid();
        return Project.Create(
            organizationId: OrgId,
            name: "Phase 4 Migration",
            description: "Move projects feature into production.",
            ownerUserId: ownerId,
            atUtc: Now);
    }

    // --- Create ---

    [Fact]
    public void Create_initializes_with_Planning_status_and_owner_membership()
    {
        var project = CreateWithOwner(out var ownerId);

        project.OrganizationId.Should().Be(OrgId);
        project.Name.Should().Be("Phase 4 Migration");
        project.Description.Should().Be("Move projects feature into production.");
        project.Status.Should().Be(ProjectStatus.Planning);
        project.OwnerUserId.Should().Be(ownerId);
        project.Members.Should().ContainSingle();
        project.Members[0].UserId.Should().Be(ownerId);
        project.Members[0].Role.Should().Be(ProjectMemberRole.Owner);
    }

    [Fact]
    public void Create_rejects_empty_organization_id()
    {
        var act = () => Project.Create(Guid.Empty, "p", "", Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_owner_id()
    {
        var act = () => Project.Create(OrgId, "p", "", Guid.Empty, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_name_over_100_chars()
    {
        var longName = new string('a', 101);
        var act = () => Project.Create(OrgId, longName, "", Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_description_over_2000_chars()
    {
        var longDesc = new string('a', 2001);
        var act = () => Project.Create(OrgId, "p", longDesc, Guid.NewGuid(), Now);
        act.Should().Throw<ArgumentException>();
    }

    // --- Status transitions ---

    [Fact]
    public void ChangeStatus_Planning_to_Active_succeeds()
    {
        var project = CreateWithOwner(out _);
        project.ChangeStatus(ProjectStatus.Active, null, Now);
        project.Status.Should().Be(ProjectStatus.Active);
    }

    [Fact]
    public void ChangeStatus_Active_to_Completed_succeeds()
    {
        var project = CreateWithOwner(out _);
        project.ChangeStatus(ProjectStatus.Active, null, Now);
        project.ChangeStatus(ProjectStatus.Completed, null, Now);
        project.Status.Should().Be(ProjectStatus.Completed);
    }

    [Fact]
    public void ChangeStatus_Completed_back_to_Active_succeeds_as_reopen()
    {
        var project = CreateWithOwner(out _);
        project.ChangeStatus(ProjectStatus.Active, null, Now);
        project.ChangeStatus(ProjectStatus.Completed, null, Now);
        project.ChangeStatus(ProjectStatus.Active, null, Now);
        project.Status.Should().Be(ProjectStatus.Active);
    }

    [Fact]
    public void ChangeStatus_Planning_directly_to_Completed_is_rejected()
    {
        var project = CreateWithOwner(out _);
        var act = () => project.ChangeStatus(ProjectStatus.Completed, null, Now);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void ChangeStatus_None_target_is_rejected()
    {
        var project = CreateWithOwner(out _);
        var act = () => project.ChangeStatus(ProjectStatus.None, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangeStatus_to_same_value_is_noop()
    {
        var project = CreateWithOwner(out _);
        var originalUpdatedAt = project.UpdatedAtUtc;
        project.ChangeStatus(ProjectStatus.Planning, null, Now.AddSeconds(1));
        project.Status.Should().Be(ProjectStatus.Planning);
        project.UpdatedAtUtc.Should().Be(originalUpdatedAt);
    }

    // --- Dates ---

    [Fact]
    public void SetDates_with_due_before_start_throws()
    {
        var project = CreateWithOwner(out _);
        var start = Now;
        var due = Now.AddDays(-1);
        var act = () => project.SetDates(start, due, null, Now);
        var ex = act.Should().Throw<DomainException>().Which;
        ex.ErrorCode.Should().Be("INVALID_PROJECT_DATES");
    }

    [Fact]
    public void SetDates_with_only_start_succeeds()
    {
        var project = CreateWithOwner(out _);
        project.SetDates(Now, null, null, Now);
        project.StartDateUtc.Should().Be(Now);
        project.DueDateUtc.Should().BeNull();
    }

    [Fact]
    public void SetDates_with_start_and_due_after_start_succeeds()
    {
        var project = CreateWithOwner(out _);
        var start = Now;
        var due = Now.AddDays(7);
        project.SetDates(start, due, null, Now);
        project.StartDateUtc.Should().Be(start);
        project.DueDateUtc.Should().Be(due);
    }

    // --- AddMember ---

    [Fact]
    public void AddMember_adds_with_requested_role()
    {
        var project = CreateWithOwner(out _);
        var userId = Guid.NewGuid();

        var member = project.AddMember(userId, ProjectMemberRole.Contributor, Now);

        member.UserId.Should().Be(userId);
        member.Role.Should().Be(ProjectMemberRole.Contributor);
        member.ProjectId.Should().Be(project.Id);
        project.Members.Should().Contain(m => m.UserId == userId);
    }

    [Fact]
    public void AddMember_rejects_duplicate()
    {
        var project = CreateWithOwner(out var ownerId);
        project.AddMember(Guid.NewGuid(), ProjectMemberRole.Reader, Now);
        var act = () => project.AddMember(ownerId, ProjectMemberRole.Reader, Now);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddMember_rejects_Owner_role()
    {
        var project = CreateWithOwner(out _);
        var act = () => project.AddMember(Guid.NewGuid(), ProjectMemberRole.Owner, Now);
        act.Should().Throw<ArgumentException>();
    }

    // --- RemoveMember ---

    [Fact]
    public void RemoveMember_removes_a_non_owner()
    {
        var project = CreateWithOwner(out _);
        var userId = Guid.NewGuid();
        project.AddMember(userId, ProjectMemberRole.Contributor, Now);

        project.RemoveMember(userId, Now);

        project.Members.Should().NotContain(m => m.UserId == userId);
    }

    [Fact]
    public void RemoveMember_rejects_removing_the_owner()
    {
        var project = CreateWithOwner(out var ownerId);
        project.AddMember(Guid.NewGuid(), ProjectMemberRole.Reader, Now);
        var act = () => project.RemoveMember(ownerId, Now);
        act.Should().Throw<InvalidOperationException>();
    }

    // --- TransferOwnership ---

    [Fact]
    public void TransferOwnership_promotes_target_and_demotes_old_owner_to_Contributor()
    {
        var project = CreateWithOwner(out var oldOwnerId);
        var newOwnerId = Guid.NewGuid();
        project.AddMember(newOwnerId, ProjectMemberRole.Reader, Now);

        project.TransferOwnership(newOwnerId, null, Now);

        project.OwnerUserId.Should().Be(newOwnerId);
        project.Members.Single(m => m.UserId == newOwnerId).Role.Should().Be(ProjectMemberRole.Owner);
        project.Members.Single(m => m.UserId == oldOwnerId).Role.Should().Be(ProjectMemberRole.Contributor);
    }

    [Fact]
    public void TransferOwnership_rejects_target_that_is_not_a_member()
    {
        var project = CreateWithOwner(out _);
        var act = () => project.TransferOwnership(Guid.NewGuid(), null, Now);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TransferOwnership_rejects_self()
    {
        var project = CreateWithOwner(out var ownerId);
        var act = () => project.TransferOwnership(ownerId, null, Now);
        act.Should().Throw<InvalidOperationException>();
    }
}
