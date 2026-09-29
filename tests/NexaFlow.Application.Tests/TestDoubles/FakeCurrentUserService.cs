using NexaFlow.Application.Abstractions;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     In-memory ICurrentUserService for unit tests. Settable properties — the test
/// directly assigns UserId / IsAuthenticated to simulate different scenarios.
/// </summary>
public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? IPAddress { get; set; } = "127.0.0.1";
    public string? TraceId { get; set; } = "test-trace";
}
