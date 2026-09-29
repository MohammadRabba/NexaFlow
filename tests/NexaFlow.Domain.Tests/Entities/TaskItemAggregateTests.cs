using FluentAssertions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

public sealed class TaskItemAggregateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ReporterId = Guid.NewGuid();

    private static TaskItem Create() => TaskItem.Create(
        ProjectId, OrgId, "Implement auth", "Add JWT bearer", ReporterId,
        TaskPriority.High, null, Now);

    // --- Create ---

    [Fact]
    public void Create_sets_defaults_Todo_and_Medium_priority()
    {
        var task = TaskItem.Create(ProjectId, OrgId, "T", "", ReporterId, TaskPriority.Medium, null, Now);
        task.Status.Should().Be(TaskItemStatus.Todo);
        task.Priority.Should().Be(TaskPriority.Medium);
        task.ReporterId.Should().Be(ReporterId);
        task.AssigneeId.Should().BeNull();
        task.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_empty_title()
    {
        var act = () => TaskItem.Create(ProjectId, OrgId, "", "", ReporterId, TaskPriority.Medium, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_title_over_200_chars()
    {
        var act = () => TaskItem.Create(ProjectId, OrgId, new string('a', 201), "", ReporterId, TaskPriority.Medium, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_project_or_org_or_reporter()
    {
        var act1 = () => TaskItem.Create(Guid.Empty, OrgId, "T", "", ReporterId, TaskPriority.Medium, null, Now);
        var act2 = () => TaskItem.Create(ProjectId, Guid.Empty, "T", "", ReporterId, TaskPriority.Medium, null, Now);
        var act3 = () => TaskItem.Create(ProjectId, OrgId, "T", "", Guid.Empty, TaskPriority.Medium, null, Now);
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_None_priority()
    {
        var act = () => TaskItem.Create(ProjectId, OrgId, "T", "", ReporterId, TaskPriority.None, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    // --- State machine: all valid transitions ---

    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Cancelled)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Review)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Cancelled)]
    [InlineData(TaskItemStatus.Review, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Review, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Review, TaskItemStatus.Cancelled)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Cancelled, TaskItemStatus.Todo)]
    public void ChangeStatus_valid_transitions_succeed(TaskItemStatus from, TaskItemStatus to)
    {
        var task = Create();
        // Walk to the `from` status via a valid path.
        WalkToStatus(task, from);
        task.ChangeStatus(to, null, Now);
        task.Status.Should().Be(to);
    }

    // --- State machine: all invalid transitions ---

    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Review)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Todo)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Review)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Cancelled)]
    [InlineData(TaskItemStatus.Cancelled, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Cancelled, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Cancelled, TaskItemStatus.Review)]
    [InlineData(TaskItemStatus.Review, TaskItemStatus.Todo)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Todo)]
    public void ChangeStatus_invalid_transitions_throw(TaskItemStatus from, TaskItemStatus to)
    {
        var task = Create();
        WalkToStatus(task, from);
        var act = () => task.ChangeStatus(to, null, Now);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void ChangeStatus_to_None_throws()
    {
        var task = Create();
        var act = () => task.ChangeStatus(TaskItemStatus.None, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangeStatus_same_value_is_noop()
    {
        var task = Create();
        var originalUpdatedAt = task.UpdatedAtUtc;
        task.ChangeStatus(TaskItemStatus.Todo, null, Now.AddSeconds(1));
        task.Status.Should().Be(TaskItemStatus.Todo);
        task.UpdatedAtUtc.Should().Be(originalUpdatedAt);
    }

    // --- Assignment ---

    [Fact]
    public void Assign_sets_assignee()
    {
        var task = Create();
        var userId = Guid.NewGuid();
        task.Assign(userId, null, Now);
        task.AssigneeId.Should().Be(userId);
    }

    [Fact]
    public void Assign_to_null_unassigns()
    {
        var task = Create();
        task.Assign(Guid.NewGuid(), null, Now);
        task.Assign(null, null, Now.AddSeconds(1));
        task.AssigneeId.Should().BeNull();
    }

    // --- Soft delete ---

    [Fact]
    public void SoftDelete_prevents_further_mutations()
    {
        var task = Create();
        task.SoftDelete(ReporterId, Now);

        var act1 = () => task.Update("New", null, null, null, false, null, Now);
        var act2 = () => task.ChangeStatus(TaskItemStatus.InProgress, null, Now);
        var act3 = () => task.Assign(Guid.NewGuid(), null, Now);

        act1.Should().Throw<InvalidOperationException>();
        act2.Should().Throw<InvalidOperationException>();
        act3.Should().Throw<InvalidOperationException>();
    }

    // --- Update ---

    [Fact]
    public void Update_changes_title_and_priority()
    {
        var task = Create();
        task.Update("New Title", "New Desc", TaskPriority.Critical, null, false, null, Now);
        task.Title.Should().Be("New Title");
        task.Description.Should().Be("New Desc");
        task.Priority.Should().Be(TaskPriority.Critical);
    }

    [Fact]
    public void Update_due_date_when_updateDueDate_true()
    {
        var task = Create();
        var due = Now.AddDays(7);
        task.Update(null, null, null, due, true, null, Now);
        task.DueDateUtc.Should().Be(due);
    }

    [Fact]
    public void Update_null_fields_leave_unchanged()
    {
        var task = Create();
        var originalTitle = task.Title;
        var originalPriority = task.Priority;
        task.Update(null, null, null, null, false, null, Now);
        task.Title.Should().Be(originalTitle);
        task.Priority.Should().Be(originalPriority);
    }

    // --- Helper: walk to a target status via valid transitions ---

    private static void WalkToStatus(TaskItem task, TaskItemStatus target)
    {
        if (target == TaskItemStatus.Todo) return;
        if (target == TaskItemStatus.InProgress)
        {
            task.ChangeStatus(TaskItemStatus.InProgress, null, Now);
            return;
        }
        if (target == TaskItemStatus.Review)
        {
            task.ChangeStatus(TaskItemStatus.InProgress, null, Now);
            task.ChangeStatus(TaskItemStatus.Review, null, Now);
            return;
        }
        if (target == TaskItemStatus.Done)
        {
            task.ChangeStatus(TaskItemStatus.InProgress, null, Now);
            task.ChangeStatus(TaskItemStatus.Done, null, Now);
            return;
        }
        if (target == TaskItemStatus.Cancelled)
        {
            task.ChangeStatus(TaskItemStatus.Cancelled, null, Now);
            return;
        }
        throw new InvalidOperationException($"Unknown target {target}");
    }
}
