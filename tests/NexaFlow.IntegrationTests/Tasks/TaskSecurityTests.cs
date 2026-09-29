using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Infrastructure.Persistence;
using NexaFlow.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NexaFlow.IntegrationTests.Tasks;

public sealed class TaskSecurityTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TaskSecurityTests(PostgreSqlFixture fixture, ITestOutputHelper output)
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
        SetupProjectAsync(string userEmail)
    {
        var factory = new NexaFlowWebApplicationFactory(_fixture.Container);
        factory.CreateClient();
        await factory.ApplyMigrationsAsync();

        var (userId, email, _) = await factory.Auth.RegisterUserAsync(userEmail);
        var (orgId, _) = await factory.Auth.CreateOrganizationAsync("Org " + userEmail, userId);
        var token = factory.Auth.IssueAccessToken(userId, email);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        var resp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "Test Project",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        resp.EnsureSuccessStatusCode();
        var project = await resp.Content.ReadFromJsonAsync<TestProjectDto>();

        return (factory, userId, email, token, orgId, project!.Id);
    }

    [Fact]
    public async Task User_cannot_access_task_from_another_organization()
    {
        RequireDocker();
        var (factoryA, userAId, emailA, tokenA, orgAId, projectAId) = await SetupProjectAsync("alice@example.com");
        var clientA = factoryA.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        clientA.DefaultRequestHeaders.Add("X-Organization-Id", orgAId.ToString());

        // Create a task in project A.
        var createResp = await clientA.PostAsJsonAsync($"/api/projects/{projectAId}/tasks", new
        {
            Title = "Task A",
            Description = "",
            Priority = TaskPriority.Medium,
            AssigneeId = (Guid?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        createResp.EnsureSuccessStatusCode();
        var task = await createResp.Content.ReadFromJsonAsync<TestTaskDto>();

        // User B in a different org.
        var (userBId, emailB, _) = await factoryA.Auth.RegisterUserAsync("bob@example.com");
        var (orgBId, _) = await factoryA.Auth.CreateOrganizationAsync("BarCorp", userBId);
        var tokenB = factoryA.Auth.IssueAccessToken(userBId, emailB);

        var clientB = factoryA.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        clientB.DefaultRequestHeaders.Add("X-Organization-Id", orgBId.ToString());

        // User B tries to access task A.
        var resp = await clientB.GetAsync($"/api/projects/{projectAId}/tasks/{task!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_cannot_access_task_in_another_project_without_project_access()
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

        // Create project A + task.
        var respA = await ownerClient.PostAsJsonAsync("/api/projects", new
        {
            Name = "Proj A",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var projectA = await respA.Content.ReadFromJsonAsync<TestProjectDto>();

        var taskResp = await ownerClient.PostAsJsonAsync($"/api/projects/{projectA!.Id}/tasks", new
        {
            Title = "Task A",
            Description = "",
            Priority = TaskPriority.Medium,
            AssigneeId = (Guid?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var task = await taskResp.Content.ReadFromJsonAsync<TestTaskDto>();

        // Create project B (same org, different project).
        var respB = await ownerClient.PostAsJsonAsync("/api/projects", new
        {
            Name = "Proj B",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var projectB = await respB.Content.ReadFromJsonAsync<TestProjectDto>();

        // Access task A via project B's route → must fail.
        var resp = await ownerClient.GetAsync($"/api/projects/{projectB!.Id}/tasks/{task!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Task A belongs to project B; accessing it via project B's route should 404.");
    }

    [Fact]
    public async Task Reader_can_read_but_not_update_tasks()
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

        var projResp = await ownerClient.PostAsJsonAsync("/api/projects", new
        {
            Name = "P1",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var project = await projResp.Content.ReadFromJsonAsync<TestProjectDto>();

        var taskResp = await ownerClient.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new
        {
            Title = "T1",
            Description = "",
            Priority = TaskPriority.Medium,
            AssigneeId = (Guid?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var task = await taskResp.Content.ReadFromJsonAsync<TestTaskDto>();

        // Add a Reader.
        var (readerId, readerEmail, _) = await factory.Auth.RegisterUserAsync("reader@example.com");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Add(ProjectMember.CreateInternal(project.Id, orgId, readerId, ProjectMemberRole.Reader, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var readerToken = factory.Auth.IssueAccessToken(readerId, readerEmail);

        var readerClient = factory.CreateClient();
        readerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        readerClient.DefaultRequestHeaders.Add("X-Organization-Id", orgId.ToString());

        // Reader can read.
        var getResp = await readerClient.GetAsync($"/api/projects/{project.Id}/tasks/{task!.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Reader cannot update.
        var updateResp = await readerClient.PutAsJsonAsync($"/api/projects/{project.Id}/tasks/{task.Id}",
            new { NewTitle = "Hacked", NewDescription = (string?)null, NewPriority = (TaskPriority?)null, DueDateUtc = (DateTimeOffset?)null, UpdateDueDate = false });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_status_transition_returns_conflict()
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

        var projResp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "P1",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var project = await projResp.Content.ReadFromJsonAsync<TestProjectDto>();

        var taskResp = await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new
        {
            Title = "T1",
            Description = "",
            Priority = TaskPriority.Medium,
            AssigneeId = (Guid?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var task = await taskResp.Content.ReadFromJsonAsync<TestTaskDto>();

        // Todo → Done is invalid (must go through InProgress first).
        var resp = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks/{task!.Id}/status",
            new { NewStatus = TaskItemStatus.Done });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Soft_deleted_task_not_returned_by_queries()
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

        var projResp = await client.PostAsJsonAsync("/api/projects", new
        {
            Name = "P1",
            Description = "",
            StartDateUtc = (DateTimeOffset?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var project = await projResp.Content.ReadFromJsonAsync<TestProjectDto>();

        var taskResp = await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tasks", new
        {
            Title = "T1",
            Description = "",
            Priority = TaskPriority.Medium,
            AssigneeId = (Guid?)null,
            DueDateUtc = (DateTimeOffset?)null
        });
        var task = await taskResp.Content.ReadFromJsonAsync<TestTaskDto>();

        // Delete the task.
        await client.DeleteAsync($"/api/projects/{project.Id}/tasks/{task!.Id}");

        // GET single → 404.
        var getResp = await client.GetAsync($"/api/projects/{project.Id}/tasks/{task.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // GET list → task not in results.
        var listResp = await client.GetAsync($"/api/projects/{project.Id}/tasks");
        var list = await listResp.Content.ReadFromJsonAsync<TestPagedResult<TestTaskSummary>>();
        list!.Items.Should().NotContain(t => t.Id == task.Id);
    }

    private sealed record TestTaskDto(Guid Id, Guid ProjectId, string Title, string Description,
        TaskItemStatus Status, TaskPriority Priority, Guid? AssigneeId, Guid ReporterId,
        DateTimeOffset? DueDateUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

    private sealed record TestTaskSummary(Guid Id, string Title, TaskItemStatus Status, TaskPriority Priority,
        Guid? AssigneeId, DateTimeOffset? DueDateUtc);

    private sealed record TestPagedResult<T>(List<T> Items, int Page, int PageSize, long TotalCount, int TotalPages);

    private sealed record TestProjectDto(Guid Id, Guid OrganizationId, string Name, string Description,
        Domain.Enums.ProjectStatus Status, DateTimeOffset? StartDateUtc, DateTimeOffset? DueDateUtc,
        Guid OwnerUserId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
}
