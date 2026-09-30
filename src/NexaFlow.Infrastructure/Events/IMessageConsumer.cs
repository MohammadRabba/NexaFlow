namespace NexaFlow.Infrastructure.Events;

public interface IMessageConsumer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
