using System.Text.Json;
using FluentAssertions;
using NexaFlow.Domain.Events.Tasks;
using NexaFlow.Infrastructure.Events;
using Xunit;

namespace NexaFlow.Application.Tests.Events;

public sealed class JsonEventSerializerTests
{
    private readonly JsonEventSerializer _serializer = new();
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serialize_TaskCreatedEvent_round_trips()
    {
        var evt = new TaskCreatedEvent(
            EventId: Guid.NewGuid(), TaskId: Guid.NewGuid(), ProjectId: Guid.NewGuid(),
            OrganizationId: Guid.NewGuid(), ReporterId: Guid.NewGuid(),
            Title: "Test Task", OccurredOnUtc: DateTimeOffset.UtcNow);
        var (eventType, payload) = _serializer.Serialize(evt);
        eventType.Should().Be("NexaFlow.Domain.Events.Tasks.TaskCreatedEvent");
        var deserialized = JsonSerializer.Deserialize<TaskCreatedEvent>(payload, WebOptions);
        deserialized!.TaskId.Should().Be(evt.TaskId);
        deserialized.Title.Should().Be(evt.Title);
        deserialized.EventId.Should().Be(evt.EventId);
    }

    [Fact]
    public void Serialize_TaskAssignedEvent_round_trips()
    {
        var evt = new TaskAssignedEvent(
            EventId: Guid.NewGuid(), TaskId: Guid.NewGuid(), OrganizationId: Guid.NewGuid(),
            TaskTitle: "Implement auth", AssigneeId: Guid.NewGuid(), OccurredOnUtc: DateTimeOffset.UtcNow);
        var (_, payload) = _serializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<TaskAssignedEvent>(payload, WebOptions);
        deserialized!.AssigneeId.Should().Be(evt.AssigneeId);
        deserialized.TaskTitle.Should().Be(evt.TaskTitle);
        deserialized.OrganizationId.Should().Be(evt.OrganizationId);
        deserialized.EventId.Should().Be(evt.EventId);
    }

    [Fact]
    public void Serialize_TaskStatusChangedEvent_round_trips()
    {
        var evt = new TaskStatusChangedEvent(
            Guid.NewGuid(), Guid.NewGuid(), "Todo", "InProgress", DateTimeOffset.UtcNow);
        var (_, payload) = _serializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<TaskStatusChangedEvent>(payload, WebOptions);
        deserialized!.FromStatus.Should().Be("Todo");
        deserialized.ToStatus.Should().Be("InProgress");
        deserialized.EventId.Should().Be(evt.EventId);
    }

    [Fact]
    public void Serialize_TaskCommentAddedEvent_round_trips()
    {
        var evt = new TaskCommentAddedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var (_, payload) = _serializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<TaskCommentAddedEvent>(payload, WebOptions);
        deserialized!.CommentId.Should().Be(evt.CommentId);
        deserialized.EventId.Should().Be(evt.EventId);
    }

    [Fact]
    public void Serialize_includes_EventId_in_payload()
    {
        var eventId = Guid.NewGuid();
        var evt = new TaskAssignedEvent(
            eventId, Guid.NewGuid(), Guid.NewGuid(), "T", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var (_, payload) = _serializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<TaskAssignedEvent>(payload, WebOptions);
        deserialized!.EventId.Should().Be(eventId);
    }

    [Fact]
    public void Deserialize_malformed_json_throws()
    {
        var act = () => JsonSerializer.Deserialize<TaskAssignedEvent>("{not json}", WebOptions);
        act.Should().Throw<JsonException>();
    }
}
