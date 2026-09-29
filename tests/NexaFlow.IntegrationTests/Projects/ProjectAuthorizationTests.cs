using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexaFlow.Domain.Enums;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NexaFlow.IntegrationTests.Projects;

/// <summary>
///     Project-level integration tests — resource authorization + cross-tenant isolation.
/// Requires Docker (Testcontainers PostgreSQL). If Docker is unavailable, tests fail
/// with an explicit 'Docker unavailable — test implemented but not executed' message.
/// </summary>
public sealed class ProjectAuthorizationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ProjectAuthorizationTests(PostgreSqlFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        if (!fixture.IsDockerAvailable) _output.WriteLine($"SKIP: {fixture.SkipReason}");
    }

    private void RequireDocker()
    {
        if (!_fixture.IsDockerAvailable)
            Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment.");
    }

    private async Task<(NexaFlowWebApplicationFactory factory, Guid userId, string email, string token, Guid orgId, Guid projectId)>
        SetupUserOrgAndProjectAsync(string userEmail)
    {
        var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userId, email, _) = await factory.Auth.RegisterUserAsync(userEmail);
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Org " + userEmail, userId);
        var token = factory.Auth.IssueAccessToken(userId, email);

        // Create a project via the API.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var resp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "Test Project",
            Description = "Phase 4 integration test project.",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        resp.EnsureSuccessStatusCode();
        var project = await resp.Content.ReadFromJsonAsync<TestProjectDto>();

        return (factory, userId, email, token, orgId, project!.Id);
    }

    /// <summary>
    ///     Test — User A can create and GET their own project.
    /// </summary>
    [Fact]
    public async Task User_can_create_and_get_their_own_project()
    {
        RequireDocker();
        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userId, email, _) = await factory.Auth.RegisterUserAsync("creator@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", userId);
        var token = factory.Auth.IssueAccessToken(userId, email);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        // Create
        var createResp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "Migration Plan",
            Description = "Move to .NET 10",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var project = await createResp.Content.ReadFromJsonAsync<TestProjectDto>();
        project!.Name.Should().Be("Migration Plan");
        project.OwnerUserId.Should().Be(userId);

        // GET
        var getResp = await client.GetAsync($"/api/projects/{project.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     Test — Cross-tenant: user A's project is not accessible to user B (404, no enumeration leak).
    /// </summary>
    [Fact]
    public async Task Cross_tenant_project_access_returns_404()
    {
        RequireDocker();
        // User A creates org + project.
        var (factoryA, userAId, emailA, tokenA, orgAId, projectAId) =
            await SetupUserOrgAndProjectAsync("alice@example.com");

        // User B in a DIFFERENT org.
        var (userBId, emailB, _) = await factoryA.Auth.RegisterUserAsync("bob@example.com");
        var (orgBId, _) = await factoryA.Auth.CreateOrganizationAsync("BarCorp", userBId);
        var tokenB = factoryA.Auth.IssueAccessToken(userBId, emailB);

        var clientB = factoryA.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        // User B sets X-Organization-Id = orgB (their own org).
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", orgBId.ToString());

        // User B tries to GET project A (in org A) by URL id.
        var resp = await clientB.GetAsync($"/api/projects/{projectAId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "User B is in org B; project A is in org A — cross-tenant guard fires (404).");
    }

    /// <summary>
    ///     Test — A non-member (same org, different project) cannot access a project they're not a member of.
    /// </summary>
    [Fact]
    public async Task Non_member_in_same_org_cannot_access_project_returns_404()
    {
        RequireDocker();
        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        // Owner creates org + project.
        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", ownerId);
        var ownerToken = factory.Auth.IssueAccessToken(ownerId, ownerEmail);

        var ownerClient = factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        ownerClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var createResp = await ownerClient.PostAsJsonAsync("/api/projects", new
        {
            Name = "Private Project", Description = "", StartDateUtc = (DateTimeOffset?)null, DueDateUtc = (DateTimeOffset?)null
        });
        var project = await createResp.Content.ReadFromJsonAsync<TestProjectDto>();

        // Another user in the same org, but NOT a project member.
        var (otherId, otherEmail, _) = await factory.Auth.RegisterUserAsync("other@example.com");
        await factory.Auth.AddMembershipAsync(orgId, otherId, OrganizationRole.Member);
        var otherToken = factory.Auth.IssueAccessToken(otherId, otherEmail);

        var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        otherClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var resp = await otherClient.GetAsync($"/api/projects/{project!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "User is a member of the org but NOT a member of this project — 404 (resource-level authz).");
    }

    /// <summary>
    ///     Test — Reader cannot update a project (404 — role check, no enumeration leak).
    /// </summary>
    [Fact]
    public async Task Reader_cannot_update_project_returns_404()
    {
        RequireDocker();
        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", ownerId);
        var ownerToken = factory.Auth.IssueAccessToken(ownerId, ownerEmail);

        var ownerClient = factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        ownerClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var createResp = await ownerClient.PostAsJsonAsync("/api/projects", new
        {
            Name = "P1", Description = "", StartDateUtc = (DateTimeOffset?)null, DueDateUtc = (DateTimeOffset?)null
        });
        var project = await createResp.Content.ReadFromJsonAsync<TestProjectDto>();

        // Add a Reader.
        var (readerId, readerEmail, _) = await factory.Auth.RegisterUserAsync("reader@example.com");
        var readerToken = factory.Auth.IssueAccessToken(readerId, readerEmail);

        // Add reader to project directly (bypass API for test setup).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Add(Domain.Entities.ProjectMember.CreateInternal(
                project!.Id, orgId, readerId, Domain.Enums.ProjectMemberRole.Reader, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var readerClient = factory.CreateClient();
        readerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        readerClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var resp = await readerClient.PutAsJsonAsync($"/api/projects/{project.Id}",
            new { NewName = "Hacked", NewDescription = (string?)null, NewStatus = (ProjectStatus?)null, UpdateDates = false,
                  StartDateUtc = (DateTimeOffset?)null, DueDateUtc = (DateTimeOffset?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Reader cannot update — ProjectAccess requires Contributor minimum role.");
    }

    /// <summary>
    ///     Test — Client-injected OrganizationId in project payload is ignored; server derives Owner from the current user.
    /// </summary>
    [Fact]
    public async Task Client_injected_OrganizationId_in_project_payload_is_ignored()
    {
        RequireDocker();
        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", ownerId);
        var token = factory.Auth.IssueAccessToken(ownerId, ownerEmail);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        // The request body has no OrganizationId field — the API derives it from the
        // resolved tenant. If we tried to inject one, it would be ignored (the DTO
        // doesn't have the field).
        var resp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "New Project",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null,
            OrganizationId = Guid.NewGuid()  // injected — but the DTO doesn't have this field
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var project = await resp.Content.ReadFromJsonAsync<TestProjectDto>();
        project!.OrganizationId.Should().Be(orgId, "Server ignores the injected OrganizationId — uses the resolved tenant.");
        project.OwnerUserId.Should().Be(ownerId, "Server sets the current user as Owner.");
    }

    /// <summary>
    ///     Test — Mutations cannot move a project between tenants (OrganizationId is immutable).
    /// </summary>
    [Fact]
    public async Task Mutations_cannot_move_project_between_tenants()
    {
        RequireDocker();
        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", ownerId);
        var token = factory.Auth.IssueAccessToken(ownerId, ownerEmail);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var createResp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "P1", Description = "", StartDateUtc = (DateTimeOffset?)null, DueDateUtc = (DateTimeOffset?)null
        });
        var project = await createResp.Content.ReadFromJsonAsync<TestProjectDto>();

        // The UpdateProjectRequest doesn't have an OrganizationId field — the server cannot
        // move the project to a different org. The UpdateProjectCommand only accepts name,
        // description, status, dates — none of which can change OrganizationId.
        var updateResp = await client.PutAsJsonAsync($"/api/projects/{project!.Id}",
            new { NewName = "Renamed", NewDescription = (string?)null, NewStatus = (ProjectStatus?)null, UpdateDates = false,
                  StartDateUtc = (DateTimeOffset?)null, DueDateUtc = (DateTimeOffset?)null });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the project is still in the same org.
        var getResp = await client.GetAsync($"/api/projects/{project.Id}");
        var fetched = await getResp.Content.ReadFromJsonAsync<TestProjectDto>();
        fetched!.OrganizationId.Should().Be(orgId, "OrganizationId is immutable — cannot be changed via any endpoint.");
    }

    private sealed record TestProjectDto(
        Guid Id,
        Guid OrganizationId,
        string Name,
        string Description,
        ProjectStatus Status,
        DateTimeOffset? StartDateUtc,
        DateTimeOffset? DueDateUtc,
        Guid OwnerUserId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
