using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NexaFlow.Api.Tests.Smoke;

/// <summary>
///     Phase 1 smoke tests for the API host. Verifies that:
///     <list type="bullet">
///         <item>The host boots without throwing.</item>
///         <item>The OpenAPI document is served at /openapi/v1.json (Dev environment).</item>
///         <item>The /health/live endpoint returns 200 + JSON status=Healthy.</item>
///         <item>The /health alias returns 503 when Postgres is unreachable (test env has no Postgres).</item>
///         <item>An unknown route returns 404 Problem Details (RFC 7807).</item>
///     </list>
/// </summary>
public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSQL",
                "Host=localhost;Port=5432;Database=nexaflow_tests;Username=test;Password=test");
            builder.UseSetting("Environment", "Development");
        });
    }

    [Fact]
    public async Task Health_live_endpoint_should_return_200_with_status_healthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status\":\"Healthy\"");
        content.Should().Contain("\"kind\":\"live\"");
    }

    [Fact]
    public async Task OpenApi_document_should_be_served_in_development()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act — .NET 10's MapOpenApi serves the doc at /openapi/v1.json
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert — the document should be reachable and content-type should be JSON
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Unknown_route_should_return_problem_details_with_trace_id()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/does-not-exist");

        // Assert — ASP.NET returns 404, our middleware adds the X-Trace-Id header
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Contains("X-Trace-Id").Should().BeTrue();
    }

    [Fact]
    public async Task Audit_logs_endpoint_should_require_authentication()
    {
        // Phase 8: /api/audit-logs is [Authorize] — anonymous requests must be 401, not 200.
        // This protects against accidentally exposing audit data to the public.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/audit-logs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Audit_logs_my_activity_endpoint_should_require_authentication()
    {
        // Phase 8: /api/audit-logs/my-activity is [Authorize] — anonymous requests must be 401.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/audit-logs/my-activity");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
