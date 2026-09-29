using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A comment on a task. Soft-deleted via <see cref="AggregateRoot.SoftDelete" />.
///     Section 18: Author, CreatedAt, UpdatedAt, DeletedAt. Only the author can edit;
///     the author or a project Owner can delete.
/// </summary>
public class Comment : AggregateRoot, ITenantEntity
{
    private Comment() { }

    public Guid TaskId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Body { get; private set; } = string.Empty;

    public static Comment Create(
        Guid taskId, Guid organizationId, Guid authorId, string body, DateTimeOffset atUtc)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId must not be empty.", nameof(taskId));
        if (organizationId == Guid.Empty) throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        if (authorId == Guid.Empty) throw new ArgumentException("AuthorId must not be empty.", nameof(authorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (body.Length > 5000) throw new ArgumentException("Comment body must not exceed 5000 characters.");

        return new Comment
        {
            TaskId = taskId,
            OrganizationId = organizationId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    public void Edit(string newBody, Guid? editedByUserId, DateTimeOffset atUtc)
    {
        if (IsDeleted) throw new InvalidOperationException("Cannot edit a deleted comment.");
        ArgumentException.ThrowIfNullOrWhiteSpace(newBody);
        if (newBody.Length > 5000) throw new ArgumentException("Comment body must not exceed 5000 characters.");
        if (string.Equals(Body, newBody, StringComparison.Ordinal)) return;
        Body = newBody.Trim();
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = editedByUserId;
    }
}
