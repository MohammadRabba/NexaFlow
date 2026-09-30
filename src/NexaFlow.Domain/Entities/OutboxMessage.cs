namespace NexaFlow.Domain.Entities;

/// <summary>
///     An outbox message — a domain event serialized and stored in the same DB transaction
///     as the business state change. The OutboxProcessor publishes these to RabbitMQ
///     after the transaction commits, providing at-least-once delivery (section 22).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? Error { get; set; }
}
