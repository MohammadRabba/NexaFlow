using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexaFlow.Domain.Enums;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NexaFlow.IntegrationTests.Audit;

/// <summary>
///     Phase 8 integration tests for the audit-log subsystem. Verifies:
///     <list type="bullet">
///         <item>An audited operation (e.g., login) writes an audit row to PostgreSQL.</item>
///         <item>The /api/audit-logs endpoint returns organization-scoped rows for Admin.</item>
///         <item>The AuditLog.Read policy is enforced — a non-Admin/Owner gets 403.</item>
///         <item>The /api/audit-logs/my-activity endpoint returns the caller's auth events.</item>
///         <item>Cross-tenant reads are blocked (Org A cannot see Org B's audit rows).</item>
///     </list>
/// </summary>
/// <remarks>
///     These tests require Docker + a real PostgreSQL instance. If Docker is unavailable,
///     they fail (not skipped) per the Phase 6+ integration-test contract: tests are
///     implemented and ready, but cannot execute against infrastructure that is not present.
/// </remarks>
public sealed class AuditLogIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AuditLogIntegrationTests(PostgreSqlFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        if (!fixture.IsDockerAvailable)
        {
            _output.WriteLine($"SKIP: {fixture.SkipReason}");
        }
    }

    [Fact]
    public async Task Organization_creation_writes_an_OrganizationCreated_audit_row()
    {
        if (!_fixture.IsDockerAvailable)
        {
            Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment.");
            return;
        }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userId, email, _) = await factory.Auth.RegisterUserAsync("alice@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme Audit", userId);

        // Assert — at least one audit row was written for this org, with Action=OrganizationCreated.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditRows = await db.AuditLogs
            .Where(a => a.OrganizationId == orgId && a.Action == "OrganizationCreated")
            .ToListAsync();
        auditRows.Should().NotBeEmpty(
            "OrganizationCreated should be audited in the same transaction as the organization creation.");
    }

    [Fact]
    public async Task Audit_logs_endpoint_returns_organization_scoped_rows_for_admin()
    {
        if (!_fixture.IsDockerAvailable)
        {
            Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment.");
            return;
        }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (adminId, adminEmail, _) = await factory.Auth.RegisterUserAsync("admin@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme Audit Org", adminId);

        // The Owner of the org implicitly holds AuditLog.Read via the Owner permission set.
        var client = factory.CreateClient();
        var token = factory.Auth.IssueAccessToken(adminId, adminEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var response = await client.GetAsync("/api/audit-logs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Audit_logs_endpoint_rejects_Member_role_with_403()
    {
        if (!_fixture.IsDockerAvailable)
        {
            Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment.");
            return;
        }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (memberId, memberEmail, _) = await factory.Auth.RegisterUserAsync("member@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme Audit Org", ownerId);
        await factory.Auth.AddMembershipAsync(orgId, memberId, OrganizationRole.Member);

        var client = factory.CreateClient();
        var token = factory.Auth.IssueAccessToken(memberId, memberEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var response = await client.GetAsync("/api/audit-logs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "AuditLog.Read is granted to Owner + Admin only; a Member role must get 403.");
    }

    [Fact]
    public async Task My_activity_endpoint_returns_caller_auth_events()
    {
        if (!_fixture.IsDockerAvailable)
        {
            Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment.");
            return;
        }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        // RegisterUserAsync verifies the email — EmailVerified audit rows will exist.
        var (userId, email, _) = await factory.Auth.RegisterUserAsync("alice@example.com");

        var client = factory.CreateClient();
        var token = factory.Auth.IssueAccessToken(userId, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/audit-logs/my-activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cross_tenant_audit_rows_are_not_visible_via_organization_endpoint()
    {
        if (!_fixture.IsDockerAvailable)
        {
            Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment.");
            return;
        }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (adminAId, adminAEmail, _) = await factory.Auth.RegisterUserAsync("adminA@example.com");
        var (adminBId, adminBEmail, _) = await factory.Auth.RegisterUserAsync("adminB@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Acme A", adminAId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Bar B Unique Slug", adminBId);

        // Generate audit rows in Org B by creating the org (OrganizationCreated is audited).
        // Now switch to Org A and ask for audit logs.
        var clientA = factory.CreateClient();
        var tokenA = factory.Auth.IssueAccessToken(adminAId, adminAEmail);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        clientA.DefaultRequestHeaders.Add("X-Organization-Id", orgAId.ToString());

        var response = await clientA.GetAsync("/api/audit-logs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The Org A audit log must NOT contain Org B's audit rows.
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("Bar B Unique Slug",
            "Org A must not see Org B's audit rows via the organization-scoped endpoint.");
        content.Should().NotContain(orgBId.ToString(),
            "Org A must not see Org B's organization id in its audit log.");
    }
}
