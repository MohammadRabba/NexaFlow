using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A notification for a user. Persisted before SignalR delivery (section 19 —
///     "Persist the notification before attempting real-time delivery").
///     Tenant-scoped via <see cref="OrganizationId" />.
/// </summary>
public class Notification : AggregateRoot, ITenantEntity
{
    private Notification() { }

    public Guid OrganizationId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string NotificationType { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public string? RelatedEntityType { get; private set; }

    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    /// <summary>Stable id of the source domain event. Used for consumer-side idempotency.</summary>
    public Guid? SourceEventId { get; private set; }

    public static Notification Create(
        Guid organizationId,
        Guid recipientUserId,
        string notificationType,
        string title,
        string? message,
        Guid? relatedEntityId,
        string? relatedEntityType,
        Guid? sourceEventId,
        DateTimeOffset atUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId must not be empty.", nameof(organizationId));
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("RecipientUserId must not be empty.", nameof(recipientUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Notification
        {
            OrganizationId = organizationId,
            RecipientUserId = recipientUserId,
            NotificationType = notificationType,
            Title = title.Trim(),
            Message = message,
            RelatedEntityId = relatedEntityId,
            RelatedEntityType = relatedEntityType,
            SourceEventId = sourceEventId,
            IsRead = false,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    public void MarkAsRead(DateTimeOffset atUtc)
    {
        if (IsRead) return;
        IsRead = true;
        ReadAtUtc = atUtc;
        UpdatedAtUtc = atUtc;
    }
}
