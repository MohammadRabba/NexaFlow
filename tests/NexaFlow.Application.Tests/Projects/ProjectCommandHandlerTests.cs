using FluentAssertions;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;
using Xunit;
using FakeCurrentUserService = NexaFlow.Application.Tests.TestDoubles.FakeCurrentUserService;

namespace NexaFlow.Application.Tests.Projects;

/// <summary>
///     Application-layer unit tests for the project CRUD handlers. Uses the in-memory
/// test double for IApplicationDbContext.
/// </summary>
public sealed class ProjectCommandHandlerTests
{
    private readonly InMemoryApplicationDbContext _db = new();
    private readonly FakeCurrentUserService _currentUser;
    private readonly FakeCurrentTenantService _currentTenant;
    private readonly ProjectAccess _access;

    public ProjectCommandHandlerTests()
    {
        _currentUser = new FakeCurrentUserService { UserId = Guid.NewGuid(), IsAuthenticated = true };
        _currentTenant = new FakeCurrentTenantService();
        _access = new ProjectAccess(_db, _currentUser, _currentTenant);
    }

    private (Guid orgId, Guid userId) SeedOrgAndUser()
    {
        var userId = _currentUser.UserId!.Value;
        var orgId = Guid.NewGuid();
        var org = Organization.Create("Acme", "acme", userId, DateTimeOffset.UtcNow);
        _db.Organizations.Add(org);
        _currentTenant.SetTenant(TenantId.From(org.Id));
        return (org.Id, userId);
    }

    private void SeedProjectMemberFor(Project project, Guid userId, ProjectMemberRole role)
        => _db.ProjectMembers.Add(ProjectMember.CreateInternal(
            project.Id, project.OrganizationId, userId, role, DateTimeOffset.UtcNow));

    // --- CreateProject ---

    [Fact]
    public async Task CreateProject_assigns_current_user_as_Owner_and_resolved_tenant()
    {
        var (orgId, userId) = SeedOrgAndUser();
        var handler = new CreateProjectCommandHandler(
            _db, _currentUser, _currentTenant,
            LoggerFactory.Create(_ => { }).CreateLogger<CreateProjectCommandHandler>());

        var result = await handler.Handle(
            new CreateProjectCommand("Migration", "Move to .NET 10", null, null),
            CancellationToken.None);

        result.OrganizationId.Should().Be(orgId);
        result.OwnerUserId.Should().Be(userId);
        result.Status.Should().Be(ProjectStatus.Planning);
        result.Name.Should().Be("Migration");

        // The Owner membership is created inline by Project.Create — it lives in the
        // Project's in-aggregate _members collection. In production, EF Core's change
        // tracker discovers it via the navigation; the in-memory test double does NOT
        // do that automatically, so we assert on the aggregate's Members collection
        // instead of _db.ProjectMembers.
        _db.Projects.Should().ContainSingle();
        var saved = _db.Projects[0];
        saved.Members.Should().ContainSingle();
        saved.Members[0].UserId.Should().Be(userId);
        saved.Members[0].Role.Should().Be(ProjectMemberRole.Owner);
    }

    [Fact]
    public async Task CreateProject_without_resolved_tenant_throws()
    {
        var handler = new CreateProjectCommandHandler(
            _db, _currentUser, _currentTenant,
            LoggerFactory.Create(_ => { }).CreateLogger<CreateProjectCommandHandler>());
        var act = () => handler.Handle(
            new CreateProjectCommand("p", "", null, null), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateProject_with_due_before_start_throws_domain_exception()
    {
        SeedOrgAndUser();
        var handler = new CreateProjectCommandHandler(
            _db, _currentUser, _currentTenant,
            LoggerFactory.Create(_ => { }).CreateLogger<CreateProjectCommandHandler>());
        var now = DateTimeOffset.UtcNow;
        var act = () => handler.Handle(
            new CreateProjectCommand("p", "", now, now.AddDays(-1)), CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_PROJECT_DATES");
    }

    // --- UpdateProject ---

    [Fact]
    public async Task UpdateProject_renames_when_NewName_set()
    {
        var (orgId, userId) = SeedOrgAndUser();
        var project = Project.Create(orgId, "OldName", "", userId, DateTimeOffset.UtcNow);
        _db.Projects.Add(project);
        SeedProjectMemberFor(project, userId, ProjectMemberRole.Owner);

        var handler = new UpdateProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<UpdateProjectCommandHandler>());

        await handler.Handle(
            new UpdateProjectCommand(project.Id, "NewName", null, null, null),
            CancellationToken.None);

        project.Name.Should().Be("NewName");
    }

    [Fact]
    public async Task UpdateProject_rejects_when_user_is_not_a_project_member()
    {
        var (orgId, userId) = SeedOrgAndUser();
        // Create the project as a different user — the current user is not a member.
        var otherUserId = Guid.NewGuid();
        var project = Project.Create(orgId, "p", "", otherUserId, DateTimeOffset.UtcNow);
        _db.Projects.Add(project);
        SeedProjectMemberFor(project, otherUserId, ProjectMemberRole.Owner);

        var handler = new UpdateProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<UpdateProjectCommandHandler>());

        // Even though the current user is a member of the org, they are not a member of
        // THIS project — the handler must return 404 (not 403, to avoid enumeration).
        var act = () => handler.Handle(
            new UpdateProjectCommand(project.Id, "NewName", null, null, null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateProject_rejects_cross_tenant_when_project_org_doesnt_match_resolved_tenant()
    {
        // Create org A and set the tenant to org A
        var (_, userId) = SeedOrgAndUser();

        // Create org B and a project in org B
        var orgBId = Guid.NewGuid();
        var orgB = Organization.Create("Org B", "org-b", userId, DateTimeOffset.UtcNow);
        _db.Organizations.Add(orgB);
        var projectB = Project.Create(orgBId, "Project B", "", userId, DateTimeOffset.UtcNow);
        _db.Projects.Add(projectB);
        SeedProjectMemberFor(projectB, userId, ProjectMemberRole.Owner);

        // The current tenant is org A; the URL points to projectB (in org B). Cross-tenant
        // guard must fire — returns 404, not 403.
        var handler = new UpdateProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<UpdateProjectCommandHandler>());

        var act = () => handler.Handle(
            new UpdateProjectCommand(projectB.Id, "Renamed", null, null, null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateProject_rejects_Reader_role_attempting_update()
    {
        var (orgId, userId) = SeedOrgAndUser();
        var project = Project.Create(orgId, "p", "", userId, DateTimeOffset.UtcNow);
        _db.Projects.Add(project);
        SeedProjectMemberFor(project, userId, ProjectMemberRole.Owner);

        var readerId = Guid.NewGuid();
        SeedProjectMemberFor(project, readerId, ProjectMemberRole.Reader);

        // Switch the current user to the Reader.
        _currentUser.UserId = readerId;

        var handler = new UpdateProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<UpdateProjectCommandHandler>());

        var act = () => handler.Handle(
            new UpdateProjectCommand(project.Id, "Renamed", null, null, null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateProject_change_status_uses_state_machine()
    {
        var (orgId, userId) = SeedOrgAndUser();
        var project = Project.Create(orgId, "p", "", userId, DateTimeOffset.UtcNow);
        _db.Projects.Add(project);
        SeedProjectMemberFor(project, userId, ProjectMemberRole.Owner);

        var handler = new UpdateProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<UpdateProjectCommandHandler>());

        // Planning → Active is allowed.
        await handler.Handle(
            new UpdateProjectCommand(project.Id, null, null, ProjectStatus.Active, null), CancellationToken.None);
        project.Status.Should().Be(ProjectStatus.Active);

        // Active → Planning is INVALID (state machine rejects).
        var act = () => handler.Handle(
            new UpdateProjectCommand(project.Id, null, null, ProjectStatus.Planning, null), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidStateTransitionException>();
    }

    // --- DeleteProject ---

    [Fact]
    public async Task DeleteProject_by_Owner_soft_deletes()
    {
        var (orgId, userId) = SeedOrgAndUser();
        var project = Project.Create(orgId, "p", "", userId, DateTimeOffset.UtcNow);
        _db.Projects.Add(project);
        SeedProjectMemberFor(project, userId, ProjectMemberRole.Owner);

        var handler = new DeleteProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<DeleteProjectCommandHandler>());

        await handler.Handle(new DeleteProjectCommand(project.Id), CancellationToken.None);
        project.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteProject_by_Contributor_returns_404_role_check()
    {
        var (orgId, userId) = SeedOrgAndUser();
        var project = Project.Create(orgId, "p", "", userId, DateTimeOffset.UtcNow);
        _db.Projects.Add(project);
        SeedProjectMemberFor(project, userId, ProjectMemberRole.Owner);

        var contributorId = Guid.NewGuid();
        SeedProjectMemberFor(project, contributorId, ProjectMemberRole.Contributor);
        _currentUser.UserId = contributorId;

        var handler = new DeleteProjectCommandHandler(
            _db, _currentUser, _access,
            new FakeCacheService(), LoggerFactory.Create(_ => { }).CreateLogger<DeleteProjectCommandHandler>());

        var act = () => handler.Handle(new DeleteProjectCommand(project.Id), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

// --- Fake ICurrentTenantService for tests ---

internal sealed class FakeCurrentTenantService : ICurrentTenantService
{
    private TenantId? _tenantId;
    public TenantId? OrganizationId => _tenantId;
    public bool IsTenantResolved => _tenantId is not null;
    public Guid RequireTenantId() =>
        _tenantId?.Value ?? throw new InvalidOperationException("No tenant.");
    public Guid EnsureMatchesTenantId(Guid expectedOrganizationId)
    {
        if (_tenantId is null || _tenantId.Value != expectedOrganizationId)
            throw new NotFoundException("Organization", expectedOrganizationId);
        return expectedOrganizationId;
    }
    internal void SetTenant(TenantId tenant) => _tenantId = tenant;
}
