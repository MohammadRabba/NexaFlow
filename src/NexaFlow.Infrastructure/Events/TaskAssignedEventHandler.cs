using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Events.Tasks;

namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     Handles TaskAssignedEvent: creates a notification for the assignee and pushes it via SignalR.
///     Idempotent: the Notification table has a unique constraint on SourceEventId — if the same
///     event is delivered twice (at-least-once), the second insert fails with a constraint
///     violation, which we catch and treat as a no-op.
/// </summary>
public sealed class TaskAssignedEventHandler : IEventHandler<TaskAssignedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationPusher _pusher;
    private readonly ILogger<TaskAssignedEventHandler> _logger;

    public TaskAssignedEventHandler(
        IServiceScopeFactory scopeFactory,
        INotificationPusher pusher,
        ILogger<TaskAssignedEventHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _pusher = pusher;
        _logger = logger;
    }

    public async Task HandleAsync(TaskAssignedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.AssigneeId is null)
        {
            // Task was unassigned — no notification needed.
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Idempotency check: if a notification with this SourceEventId already exists,
        // skip. The DB unique constraint on source_event_id is the authoritative guard
        // against concurrent duplicates; this check is an optimization to avoid the
        // constraint-violation exception in the common case.
        var existing = await db.Notifications
            .AsQueryable()
            .AnyAsync(n => n.SourceEventId == @event.EventId, cancellationToken);

        if (existing)
        {
            _logger.LogInformation("TaskAssignedEvent {EventId} already processed — skipping.", @event.EventId);
            return;
        }

        var notification = Notification.Create(
            organizationId: @event.OrganizationId,
            recipientUserId: @event.AssigneeId.Value,
            notificationType: "TaskAssigned",
            title: $"Task assigned: {@event.TaskTitle}",
            message: $"You have been assigned to task '{@event.TaskTitle}'.",
            relatedEntityId: @event.TaskId,
            relatedEntityType: "Task",
            sourceEventId: @event.EventId,
            atUtc: @event.OccurredOnUtc);

        db.Add(notification);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.InnerException?.Message?.Contains("uq_notifications_source_event_id") == true)
        {
            // Duplicate delivery — the unique constraint caught it. This is expected
            // under at-least-once delivery; treat as success.
            _logger.LogInformation("Duplicate TaskAssignedEvent {EventId} — constraint violation caught.", @event.EventId);
            return;
        }

        // Push to SignalR — best-effort. The notification is already persisted; if the
        // client is offline, they'll see it when they next query the API.
        await _pusher.PushAsync(@event.AssigneeId.Value, new
        {
            id = notification.Id,
            type = notification.NotificationType,
            title = notification.Title,
            message = notification.Message,
            taskId = notification.RelatedEntityId
        });

        _logger.LogInformation(
            "Notification {NotificationId} created + pushed for user {UserId} from TaskAssignedEvent {EventId}.",
            notification.Id, @event.AssigneeId, @event.EventId);
    }
}
