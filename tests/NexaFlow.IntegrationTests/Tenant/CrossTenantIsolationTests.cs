using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexaFlow.Domain.Enums;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NexaFlow.IntegrationTests.Tenant;

/// <summary>
///     Cross-tenant isolation tests — the heart of Phase 3's security verification.
///     These run against a real PostgreSQL instance via Testcontainers. If Docker is
///     unavailable in the sandbox, they skip with a clear message — they are NOT
///     reported as passed.
/// </summary>
/// <remarks>
///     Per Phase 3 directive point 15: "If Docker becomes available: RUN the actual
///     integration tests. If Docker remains unavailable: report: Integration tests
///     implemented but not executed against PostgreSQL in this environment. Do NOT
///     report those tests as passed."
/// </remarks>
public sealed class CrossTenantIsolationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CrossTenantIsolationTests(PostgreSqlFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        if (!fixture.IsDockerAvailable)
        {
            _output.WriteLine($"SKIP: {fixture.SkipReason}");
        }
    }

    /// <summary>
    ///     Test 1 — User A in Org A requests Org B resource. Expected: 404 (no enumeration leak).
    /// </summary>
    [Fact]
    public async Task User_in_org_A_requesting_org_B_resource_returns_404()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userAId, userAEmail, _) = await factory.Auth.RegisterUserAsync("alice@example.com");
        var (userBId, userBEmail, _) = await factory.Auth.RegisterUserAsync("bob@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Acme A", userAId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Bar B", userBId);

        // User A gets a token + sets X-Organization-Id = orgA (their own).
        var tokenA = factory.Auth.IssueAccessToken(userAId, userAEmail);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgAId.ToString());

        // User A requests Org B's resource (the org B's GET endpoint).
        var resp = await client.GetAsync($"/api/organizations/{orgBId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "User A is not a member of org B; the API must not reveal that org B exists.");
    }

    /// <summary>
    ///     Test 2 — User A changes X-Organization-Id to Org B (which they don't belong to).
    ///     Expected: tenant resolution middleware rejects with 404.
    /// </summary>
    [Fact]
    public async Task User_sets_X_Org_Id_to_org_they_do_not_belong_to_returns_404()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userAId, userAEmail, _) = await factory.Auth.RegisterUserAsync("alice@example.com");
        var (userBId, userBEmail, _) = await factory.Auth.RegisterUserAsync("bob@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Acme A", userAId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Bar B", userBId);

        var tokenA = factory.Auth.IssueAccessToken(userAId, userAEmail);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        // User A sets X-Organization-Id = Org B (they're not a member).
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgBId.ToString());

        var resp = await client.GetAsync($"/api/organizations/{orgBId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     Test 3 — User A attempts to modify an Org B membership. Expected: rejected.
    ///     (The X-Organization-Id is set to A, but the URL points to B's members endpoint.)
    /// </summary>
    [Fact]
    public async Task User_attempts_to_modify_org_B_membership_with_org_A_tenant_header_returns_404()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userAId, userAEmail, _) = await factory.Auth.RegisterUserAsync("alice@example.com");
        var (userBId, userBEmail, _) = await factory.Auth.RegisterUserAsync("bob@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Acme A", userAId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Bar B", userBId);

        var tokenA = factory.Auth.IssueAccessToken(userAId, userAEmail);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        // Set X-Organization-Id to A (user is a member of A)
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgAId.ToString());

        // Try to modify a "membership" in org B via URL manipulation.
        // We invite a fake user to org B (URL points to B). The cross-tenant guard
        // (EnsureMatchesTenantId) should reject with 404.
        var resp = await client.PutAsJsonAsync(
            $"/api/organizations/{orgBId}/members/{userAId}",
            new { NewRole = OrganizationRole.Admin });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     Test 4 — Admin from Org A attempts to manage Org B (X-Organization-Id = B,
    ///     but the admin is not a member of B). Expected: rejected by tenant resolution.
    /// </summary>
    [Fact]
    public async Task Admin_from_org_A_managing_org_B_returns_404()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (adminAId, adminAEmail, _) = await factory.Auth.RegisterUserAsync("admin-a@example.com");
        var (userBId, userBEmail, _) = await factory.Auth.RegisterUserAsync("user-b@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Org A", adminAId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Org B", userBId);

        // Admin A invites a member to Org A — admin should be Admin role by default
        // (since they're the Owner). Let's actually downgrade admin A to Admin role in orgA.
        // Hmm — Owner can't be downgraded via the API. Owner = Admin in terms of permissions.
        // For this test, we just need admin A to attempt org B management.

        var tokenA = factory.Auth.IssueAccessToken(adminAId, adminAEmail);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        // Admin A sets X-Organization-Id = Org B (not a member of B).
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgBId.ToString());

        // Attempts to invite a member to Org B via URL.
        var resp = await client.PostAsJsonAsync(
            $"/api/organizations/{orgBId}/members/invite",
            new { InviteeEmail = "charlie@example.com", Role = OrganizationRole.Member });
        // Either the tenant middleware rejects (404 for not-a-member) OR the cross-tenant
        // guard rejects (404 for URL/header mismatch). Either way: 404.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     Test 5 — User changes role; new role is effective without re-issuing JWT.
    ///     Setup: Owner of OrgA changes Bob's role from Member to Admin via the API;
    ///     Bob immediately gets Admin permissions on his next request (with the same
    ///     JWT issued while he was a Member).
    /// </summary>
    [Fact]
    public async Task Role_change_is_immediately_effective_without_jwt_re_issuance()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (bobId, bobEmail, _) = await factory.Auth.RegisterUserAsync("bob@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", ownerId);

        // Bob joins as Member.
        await factory.Auth.AddMembershipAsync(orgId, bobId, OrganizationRole.Member);

        var ownerToken = factory.Auth.IssueAccessToken(ownerId, ownerEmail);
        var bobToken = factory.Auth.IssueAccessToken(bobId, bobEmail);

        var ownerClient = factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        ownerClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        // Bob tries to invite a member as Member — should be 403 (Member doesn't have MemberInvite).
        var bobClient = factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
        bobClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());
        var bobResp = await bobClient.PostAsJsonAsync(
            $"/api/organizations/{orgId}/members/invite",
            new { InviteeEmail = "charlie@example.com", Role = OrganizationRole.Member });
        bobResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Member role does not grant MemberInvite permission.");

        // Owner promotes Bob to Admin.
        var promoteResp = await ownerClient.PutAsJsonAsync(
            $"/api/organizations/{orgId}/members/{bobId}",
            new { NewRole = OrganizationRole.Admin });
        promoteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Bob retries with the SAME JWT — should now succeed (Admin has MemberInvite).
        var bobRetryResp = await bobClient.PostAsJsonAsync(
            $"/api/organizations/{orgId}/members/invite",
            new { InviteeEmail = "charlie@example.com", Role = OrganizationRole.Member });
        bobRetryResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "After promotion, the same JWT grants Admin permissions — database is authoritative.");
    }

    /// <summary>
    ///     Test 6 — User removed from an organization immediately loses access.
    /// </summary>
    [Fact]
    public async Task Removed_member_immediately_loses_access()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (bobId, bobEmail, _) = await factory.Auth.RegisterUserAsync("bob@example.com");
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Acme", ownerId);
        await factory.Auth.AddMembershipAsync(orgId, bobId, OrganizationRole.Member);

        var bobToken = factory.Auth.IssueAccessToken(bobId, bobEmail);
        var bobClient = factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
        bobClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        // Bob can read org A's members.
        var beforeRemoval = await bobClient.GetAsync($"/api/organizations/{orgId}/members");
        beforeRemoval.StatusCode.Should().Be(HttpStatusCode.OK);

        // Owner removes Bob.
        var ownerToken = factory.Auth.IssueAccessToken(ownerId, ownerEmail);
        var ownerClient = factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        ownerClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());
        var removeResp = await ownerClient.DeleteAsync($"/api/organizations/{orgId}/members/{bobId}");
        removeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Bob retries with the SAME JWT — must now fail.
        var afterRemoval = await bobClient.GetAsync($"/api/organizations/{orgId}/members");
        // The tenant resolution middleware checks the membership in real-time — Bob is no
        // longer a member, so the request returns 404 (no enumeration leak — same as
        // "organization does not exist or you do not have access").
        afterRemoval.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     Test 7 — Client attempts to inject OrganizationId into the request payload.
    ///     The server ignores OrganizationId from the body — it uses only the URL id
    ///     (validated against the resolved tenant).
    /// </summary>
    [Fact]
    public async Task Client_injected_OrganizationId_in_payload_is_ignored()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (ownerId, ownerEmail, _) = await factory.Auth.RegisterUserAsync("owner@example.com");
        var (otherId, otherEmail, _) = await factory.Auth.RegisterUserAsync("other@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Org A", ownerId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Org B", otherId);

        var ownerToken = factory.Auth.IssueAccessToken(ownerId, ownerEmail);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgAId.ToString());

        // Owner A attempts to create an organization with a client-supplied slug — note the
        // command does NOT accept an OrganizationId (only Name + SlugSuggestion), so there's
        // no surface for injection. The server generates the slug and creates the org for the
        // current user — the current user becomes the Owner.
        var resp = await client.PostAsJsonAsync("/api/organizations",
            new
            {
                Name = "New Org",
                SlugSuggestion = "new-org",
                // Try to inject OrganizationId — the request DTO doesn't have this field,
                // so ASP.NET silently ignores it (default JSON deserialization behavior).
                OrganizationId = orgBId
            });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify the created org is owned by user A, not org B.
        var created = await resp.Content.ReadFromJsonAsync<TestOrganizationDto>();
        created!.OwnerUserId.Should().Be(ownerId, "The current user becomes the Owner — client-supplied OrganizationId is ignored.");
        created.Id.Should().NotBe(orgBId);
    }

    /// <summary>
    ///     Test 8 — Access a valid resource ID belonging to another organization.
    ///     Expected: safe failure (404, no enumeration leak).
    /// </summary>
    [Fact]
    public async Task Access_valid_resource_id_belonging_to_another_org_returns_404()
    {
        if (!_fixture.IsDockerAvailable) { Assert.Fail("Docker unavailable — test implemented but not executed against PostgreSQL in this environment."); return; }

        await using var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userAId, userAEmail, _) = await factory.Auth.RegisterUserAsync("alice@example.com");
        var (userBId, userBEmail, _) = await factory.Auth.RegisterUserAsync("bob@example.com");
        var (orgAId, _) = await factory.Auth.CreateOrganizationAsync("Org A", userAId);
        var (orgBId, _) = await factory.Auth.CreateOrganizationAsync("Org B", userBId);

        var tokenA = factory.Auth.IssueAccessToken(userAId, userAEmail);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        // Set X-Organization-Id = A (user is a member).
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgAId.ToString());

        // Access Org B's resource id — Org B DOES exist, just in another tenant.
        var resp = await client.GetAsync($"/api/organizations/{orgBId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Org B exists but is in another tenant — 404 (not 403) to avoid confirming its existence.");
    }

    private sealed record TestOrganizationDto(
        Guid Id,
        string Name,
        string Slug,
        Guid OwnerUserId,
        DateTimeOffset CreatedAtUtc);
}
