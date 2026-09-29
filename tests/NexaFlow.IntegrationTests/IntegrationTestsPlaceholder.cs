using FluentAssertions;
using Xunit;

namespace NexaFlow.IntegrationTests;

/// <summary>
///     Phase 1 placeholder smoke test — verifies that the IntegrationTests
///     project itself is wired up and runnable. The real integration tests
///     (cross-tenant isolation against a real Postgres instance) are added
///     in Phase 3 (Multi-Tenancy).
/// </summary>
public sealed class IntegrationTestsPlaceholder
{
    [Fact]
    public void Smoke_test_project_should_be_runnable()
    {
        const int expected = 42;
        (6 * 7).Should().Be(expected);
    }
}
