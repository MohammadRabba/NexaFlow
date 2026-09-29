using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     Join between TaskItem and Label. Unique (TaskId, LabelId) prevents duplicates.
/// </summary>
public class TaskLabel : AuditableEntity, ITenantEntity
{
    private TaskLabel() { }

    public Guid TaskId { get; private set; }
    public Guid LabelId { get; private set; }
    public Guid OrganizationId { get; private set; }

    public static TaskLabel Create(Guid taskId, Guid labelId, Guid organizationId, DateTimeOffset atUtc)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId must not be empty.", nameof(taskId));
        if (labelId == Guid.Empty) throw new ArgumentException("LabelId must not be empty.", nameof(labelId));
        if (organizationId == Guid.Empty) throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));

        return new TaskLabel
        {
            TaskId = taskId,
            LabelId = labelId,
            OrganizationId = organizationId,
            CreatedAtUtc = atUtc
        };
    }
}
