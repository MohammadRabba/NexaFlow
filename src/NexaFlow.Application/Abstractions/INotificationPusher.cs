namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction for pushing real-time notifications to SignalR clients.
///     The Api project provides a SignalR implementation; tests can provide a fake.
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, object notification);
}
