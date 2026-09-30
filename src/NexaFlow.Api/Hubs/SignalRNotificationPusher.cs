using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Api.Hubs;

/// <summary>
///     SignalR implementation of INotificationPusher. Lives in the Api project because
///     it references IHubContext<NotificationHub> (which is defined here).
/// </summary>
public sealed class SignalRNotificationPusher : INotificationPusher
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationPusher> _logger;

    public SignalRNotificationPusher(IHubContext<NotificationHub> hubContext, ILogger<SignalRNotificationPusher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PushAsync(Guid userId, object notification)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("Notification", notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR push failed for user {UserId}.", userId);
        }
    }
}
