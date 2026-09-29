using DotNet.Testcontainers.Configurations;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexaFlow.IntegrationTests.Infrastructure;

/// <summary>
///     Testcontainers fixture: spawns a disposable PostgreSQL 16 container
///     per test collection. Phase 2 uses this to run auth-flow integration
///     tests against a real database (section 35 — full-stack tests).
/// </summary>
/// <remarks>
///     <para>
///         Tests that depend on this fixture use the <c>Skip.IfDockerUnavailable()</c>
///         pattern from <c>Xunit.SkippableFact</c>. In environments without a Docker
///         daemon (CI sandboxes, restricted shells), the tests skip gracefully rather
///         than fail. In environments with Docker (local dev, GitHub Actions runners),
///         the tests run against a real Postgres instance.
///     </para>
/// </remarks>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private bool _dockerAvailable = true;
    private string _skipReason = string.Empty;

    public PostgreSqlContainer? Container { get; private set; }

    public string ConnectionString =>
        Container?.GetConnectionString() ?? "Host=localhost;Port=5432;Database=nexaflow_tests;Username=nexaflow;Password=test-password";

    public bool IsDockerAvailable => _dockerAvailable;

    public string SkipReason => _skipReason;

    public async Task InitializeAsync()
    {
        try
        {
            Container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("nexaflow_tests")
                .WithUsername("nexaflow")
                .WithPassword("test-password")
                .WithCleanUp(true)
                .Build();
            await Container.StartAsync();
            _dockerAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker daemon is not running, not installed, or not accessible.
            // Tests will skip rather than fail.
            _dockerAvailable = false;
            _skipReason = "Docker daemon is not available. " +
                "Install Docker or run in an environment with Docker access " +
                "(e.g., GitHub Actions runner) to execute these tests. " +
                $"Original error: {ex.GetType().Name}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.DisposeAsync();
        }
    }
}
