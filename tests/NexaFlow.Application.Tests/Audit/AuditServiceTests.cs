using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Audit;
using NexaFlow.Application.Tests.TestDoubles;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Application.Tests.Audit;

/// <summary>
///     Tests for <see cref="AuditService" /> — verifies:
///     <list type="bullet">
///         <item>The audit row is queued via IApplicationDbContext.Add (transactional).</item>
///         <item>Actor user id is read from the ambient ICurrentUserService when no override is supplied.</item>
///         <item>Organization id is read from the ambient ICurrentTenantService when no override is supplied.</item>
///         <item>Override parameters are honored (for auth events with no ambient context).</item>
///         <item>Empty Guid tenant is normalized to null (auth event).</item>
///         <item>IPAddress is propagated to the audit row.</item>
///     </list>
/// </summary>
public sealed class AuditServiceTests
{
    [Fact]
    public async Task RecordAsync_should_add_audit_log_to_the_DbContext_for_transactional_persistence()
    {
        // Arrange — use the in-memory DbContext so we can assert on what was added.
        var db = new InMemoryApplicationDbContext();
        var currentUser = new FakeCurrentUserService
        {
            UserId = Guid.NewGuid(),
            IsAuthenticated = true,
            IPAddress = "198.51.100.10"
        };
        var currentTenant = new FakeCurrentTenantService();
        currentTenant.SetTenant(TenantId.From(Guid.NewGuid()));
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<AuditService>();
        var service = new AuditService(db, currentUser, currentTenant, logger);

        // Act
        await service.RecordAsync(
            action: AuditAction.TaskCreated,
            entity: "Task",
            entityId: Guid.NewGuid(),
            oldValues: null,
            newValues: """{"title":"new"}""");

        // Assert — exactly one audit row was queued for persistence.
        db.AuditLogsList.Should().ContainSingle();
        var entry = db.AuditLogsList[0];
        entry.Action.Should().Be(AuditAction.TaskCreated);
        entry.Entity.Should().Be("Task");
        entry.NewValues.Should().Be("""{"title":"new"}""");
        entry.UserId.Should().Be(currentUser.UserId);
        entry.OrganizationId.Should().Be(currentTenant.OrganizationId!.Value);
        entry.IPAddress.Should().Be("198.51.100.10");
    }

    [Fact]
    public async Task RecordAsync_should_use_actor_override_when_supplied()
    {
        // Arrange — simulates an auth event where the ambient user is NOT the actor
        // (e.g., login: ambient ICurrentUserService has no authenticated user yet,
        // but we know the actor from the loaded User entity).
        var db = new InMemoryApplicationDbContext();
        var currentUser = new FakeCurrentUserService { UserId = null, IsAuthenticated = false };
        var currentTenant = new FakeCurrentTenantService(); // no tenant
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<AuditService>();
        var service = new AuditService(db, currentUser, currentTenant, logger);

        var actorOverride = Guid.NewGuid();

        // Act
        await service.RecordAsync(
            action: AuditAction.LoginSucceeded,
            entity: "User",
            entityId: actorOverride,
            actorUserIdOverride: actorOverride,
            organizationIdOverride: null);

        // Assert
        var entry = db.AuditLogsList.Single();
        entry.UserId.Should().Be(actorOverride, "the override must take priority over the (null) ambient user");
        entry.OrganizationId.Should().BeNull("auth events have no tenant");
    }

    [Fact]
    public async Task RecordAsync_should_normalize_empty_guid_tenant_to_null()
    {
        // Arrange — defensive: if ICurrentTenantService.OrganizationId is Guid.Empty
        // (a misconfiguration), treat it as "no tenant" rather than recording a fake tenant.
        var db = new InMemoryApplicationDbContext();
        var currentUser = new FakeCurrentUserService { UserId = Guid.NewGuid(), IsAuthenticated = true };
        // FakeCurrentTenantService is null by default; we cannot easily inject Guid.Empty here,
        // but we can pass an explicit Guid.Empty override to confirm normalization.
        var currentTenant = new FakeCurrentTenantService();
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<AuditService>();
        var service = new AuditService(db, currentUser, currentTenant, logger);

        // Act — pass Guid.Empty as the override; the service must normalize it to null.
        await service.RecordAsync(
            action: AuditAction.LoginFailed,
            entity: "User",
            entityId: null,
            organizationIdOverride: Guid.Empty);

        // Assert
        var entry = db.AuditLogsList.Single();
        entry.OrganizationId.Should().BeNull("Guid.Empty must be normalized to null");
    }

    [Fact]
    public async Task RecordAsync_should_throw_when_action_is_empty_so_the_handler_fails_loudly()
    {
        // Arrange
        var db = new InMemoryApplicationDbContext();
        var currentUser = new FakeCurrentUserService { UserId = Guid.NewGuid(), IsAuthenticated = true };
        var currentTenant = new FakeCurrentTenantService();
        currentTenant.SetTenant(TenantId.From(Guid.NewGuid()));
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<AuditService>();
        var service = new AuditService(db, currentUser, currentTenant, logger);

        // Act
        var act = () => service.RecordAsync(action: "", entity: "Task");

        // Assert — the AuditLog.Create factory rejects empty action; the audit service
        // does NOT swallow the exception (spec: "failure to record audit is treated as
        // a failure of the operation").
        await act.Should().ThrowAsync<ArgumentException>();
        db.AuditLogsList.Should().BeEmpty("no row was queued because the factory rejected the input");
    }

    [Fact]
    public async Task RecordAsync_should_use_ambient_tenant_when_no_override_is_supplied()
    {
        var db = new InMemoryApplicationDbContext();
        var currentUser = new FakeCurrentUserService { UserId = Guid.NewGuid(), IsAuthenticated = true };
        var currentTenant = new FakeCurrentTenantService();
        var tenantId = TenantId.From(Guid.NewGuid());
        currentTenant.SetTenant(tenantId);
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<AuditService>();
        var service = new AuditService(db, currentUser, currentTenant, logger);

        await service.RecordAsync(
            action: AuditAction.ProjectUpdated,
            entity: "Project",
            entityId: Guid.NewGuid());

        var entry = db.AuditLogsList.Single();
        entry.OrganizationId.Should().Be(tenantId.Value);
        entry.UserId.Should().Be(currentUser.UserId!.Value);
    }
}
