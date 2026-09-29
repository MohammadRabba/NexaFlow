using NexaFlow.Domain.Common;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Events.Tasks;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A task within a project. Tenant-owned via <see cref="OrganizationId" /> (mirrors the
///     project's tenant). Soft-deleted via <see cref="AggregateRoot.SoftDelete" />.
/// </summary>
public class TaskItem : AggregateRoot, ITenantEntity
{
    private TaskItem() { }

    public Guid ProjectId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public TaskItemStatus Status { get; private set; } = TaskItemStatus.Todo;
    public TaskPriority Priority { get; private set; } = TaskPriority.Medium;

    public Guid? AssigneeId { get; private set; }
    public Guid ReporterId { get; private set; }

    public DateTimeOffset? DueDateUtc { get; private set; }

    public static TaskItem Create(
        Guid projectId,
        Guid organizationId,
        string title,
        string description,
        Guid reporterId,
        TaskPriority priority,
        DateTimeOffset? dueDateUtc,
        DateTimeOffset atUtc)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > 200)
            throw new ArgumentException("Task title must not exceed 200 characters.");
        if (description.Length > 5000)
            throw new ArgumentException("Description must not exceed 5000 characters.");
        if (reporterId == Guid.Empty)
            throw new ArgumentException("ReporterId must not be empty.", nameof(reporterId));
        if (priority == TaskPriority.None)
            throw new ArgumentException("Priority must not be None.", nameof(priority));

        var task = new TaskItem
        {
            ProjectId = projectId,
            OrganizationId = organizationId,
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = TaskItemStatus.Todo,
            Priority = priority,
            ReporterId = reporterId,
            DueDateUtc = dueDateUtc,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
        task.AddDomainEvent(new TaskCreatedEvent(
            task.Id, projectId, organizationId, reporterId, task.Title, atUtc));
        return task;
    }

    public void Update(
        string? newTitle,
        string? newDescription,
        TaskPriority? newPriority,
        DateTimeOffset? newDueDateUtc,
        bool updateDueDate,
        Guid? updatedByUserId,
        DateTimeOffset atUtc)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot update a deleted task.");

        if (newTitle is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);
            if (newTitle.Length > 200)
                throw new ArgumentException("Task title must not exceed 200 characters.");
            if (!string.Equals(Title, newTitle, StringComparison.Ordinal))
                Title = newTitle.Trim();
        }

        if (newDescription is not null)
        {
            if (newDescription.Length > 5000)
                throw new ArgumentException("Description must not exceed 5000 characters.");
            if (!string.Equals(Description, newDescription, StringComparison.Ordinal))
                Description = newDescription.Trim();
        }

        if (newPriority.HasValue && newPriority.Value != TaskPriority.None && newPriority.Value != Priority)
            Priority = newPriority.Value;

        if (updateDueDate)
            DueDateUtc = newDueDateUtc;

        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
    }

    public void ChangeStatus(TaskItemStatus target, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot change status of a deleted task.");
        if (target == TaskItemStatus.None)
            throw new ArgumentException("Target status must not be None.", nameof(target));
        if (Status == target) return;

        if (!Status.CanTransitionTo(target))
            throw new InvalidStateTransitionException(nameof(TaskItem), Status.ToString(), target.ToString());

        var fromStatus = Status.ToString();
        Status = target;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
        AddDomainEvent(new TaskStatusChangedEvent(Id, fromStatus, target.ToString(), atUtc));
    }

    public void Assign(Guid? assigneeId, Guid? updatedByUserId, DateTimeOffset atUtc)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Cannot assign a deleted task.");

        AssigneeId = assigneeId;
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = updatedByUserId;
        AddDomainEvent(new TaskAssignedEvent(Id, assigneeId, atUtc));
    }
}
