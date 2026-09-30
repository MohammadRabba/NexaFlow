using System.Text.Json;
using NexaFlow.Domain.Common;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Events;

/// <summary>
///     Serializes domain events to JSON for outbox storage. Uses a stable type-name
///     convention (full type name without assembly version) so deserialization can
///     resolve types across versions. Does not deserialize arbitrary client-controlled
///     types — only types from NexaFlow.Domain.
/// </summary>
public interface IEventSerializer
{
    (string eventType, string payload) Serialize(IDomainEvent domainEvent);
}

public sealed class JsonEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public (string eventType, string payload) Serialize(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var eventType = domainEvent.GetType().FullName ?? domainEvent.GetType().Name;
        var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);
        return (eventType, payload);
    }
}
