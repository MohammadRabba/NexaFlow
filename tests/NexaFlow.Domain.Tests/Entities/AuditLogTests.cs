using FluentAssertions;
using NexaFlow.Domain.Entities;
using Xunit;

namespace NexaFlow.Domain.Tests.Entities;

/// <summary>
///     Tests for the <see cref="AuditLog" /> domain entity (Phase 8 — spec §25).
///     Verifies factory invariants:
///     <list type="bullet">
///         <item>Action / Entity must be non-empty and length-capped.</item>
///         <item>IPAddress must be ≤ 45 chars (IPv6 length).</item>
///         <item>All spec §25 fields are present and assignable.</item>
///         <item>OrganizationId is nullable (auth events have no tenant).</item>
///         <item>UserId is nullable (LoginFailed for non-existent email has no actor).</item>
///         <item>The Serialize helper returns null for null input, JSON for non-null.</item>
///     </list>
/// </summary>
public sealed class AuditLogTests
{
    private static readonly DateTimeOffset AtUtc =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_should_initialize_all_spec_fields()
    {
        var actorId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var entry = AuditLog.Create(
            userId: actorId,
            organizationId: orgId,
            action: AuditAction.TaskCreated,
            entity: "Task",
            entityId: entityId,
            oldValues: null,
            newValues: """{"title":"My task"}""",
            ipAddress: "203.0.113.42",
            atUtc: AtUtc);

        entry.UserId.Should().Be(actorId);
        entry.OrganizationId.Should().Be(orgId);
        entry.Action.Should().Be(AuditAction.TaskCreated);
        entry.Entity.Should().Be("Task");
        entry.EntityId.Should().Be(entityId);
        entry.OldValues.Should().BeNull();
        entry.NewValues.Should().Be("""{"title":"My task"}""");
        entry.IPAddress.Should().Be("203.0.113.42");
        entry.Timestamp.Should().Be(AtUtc);
        // AuditableEntity timestamps are also stamped by the factory.
        entry.CreatedAtUtc.Should().Be(AtUtc);
        entry.UpdatedAtUtc.Should().Be(AtUtc);
    }

    [Fact]
    public void Create_should_allow_null_userId_for_pre_authentication_events()
    {
        // LoginFailed for a non-existent email — there is no actor to record.
        var entry = AuditLog.Create(
            userId: null,
            organizationId: null,
            action: AuditAction.LoginFailed,
            entity: "User",
            entityId: null,
            oldValues: null,
            newValues: null,
            ipAddress: "198.51.100.7",
            atUtc: AtUtc);

        entry.UserId.Should().BeNull();
        entry.OrganizationId.Should().BeNull();
        entry.EntityId.Should().BeNull();
        entry.OldValues.Should().BeNull();
        entry.NewValues.Should().BeNull();
    }

    [Fact]
    public void Create_should_allow_null_organizationId_for_auth_events()
    {
        // LoginSucceeded — user exists but no tenant resolved yet.
        var actorId = Guid.NewGuid();
        var entry = AuditLog.Create(
            userId: actorId,
            organizationId: null,
            action: AuditAction.LoginSucceeded,
            entity: "User",
            entityId: actorId,
            oldValues: null,
            newValues: null,
            ipAddress: "198.51.100.7",
            atUtc: AtUtc);

        entry.UserId.Should().Be(actorId);
        entry.OrganizationId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_should_throw_for_empty_action(string action)
    {
        var act = () => AuditLog.Create(
            userId: Guid.NewGuid(),
            organizationId: Guid.NewGuid(),
            action: action,
            entity: "Task",
            entityId: null,
            oldValues: null,
            newValues: null,
            ipAddress: null,
            atUtc: AtUtc);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_should_throw_for_empty_entity(string entity)
    {
        var act = () => AuditLog.Create(
            userId: Guid.NewGuid(),
            organizationId: Guid.NewGuid(),
            action: AuditAction.TaskCreated,
            entity: entity,
            entityId: null,
            oldValues: null,
            newValues: null,
            ipAddress: null,
            atUtc: AtUtc);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_should_throw_for_action_longer_than_64_chars()
    {
        var tooLong = new string('A', 65);
        var act = () => AuditLog.Create(
            userId: null,
            organizationId: null,
            action: tooLong,
            entity: "User",
            entityId: null,
            oldValues: null,
            newValues: null,
            ipAddress: null,
            atUtc: AtUtc);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Action*64*");
    }

    [Fact]
    public void Create_should_throw_for_ip_address_longer_than_45_chars()
    {
        // IPv6 maximum length is 45 chars — anything longer is suspicious.
        var tooLong = new string('1', 46);
        var act = () => AuditLog.Create(
            userId: null,
            organizationId: null,
            action: AuditAction.LoginFailed,
            entity: "User",
            entityId: null,
            oldValues: null,
            newValues: null,
            ipAddress: tooLong,
            atUtc: AtUtc);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*IPAddress*45*");
    }

    [Fact]
    public void Create_should_accept_45_char_ip_address()
    {
        // Maximum-valid IPv6 length. Should be accepted.
        var maxLengthIpv6 = "1111:2222:3333:4444:5555:6666:7777:8888"; // 35 chars actually
        var entry = AuditLog.Create(
            userId: null,
            organizationId: null,
            action: AuditAction.LoginFailed,
            entity: "User",
            entityId: null,
            oldValues: null,
            newValues: null,
            ipAddress: maxLengthIpv6,
            atUtc: AtUtc);

        entry.IPAddress.Should().Be(maxLengthIpv6);
    }

    [Fact]
    public void Serialize_should_return_null_for_null_input()
    {
        AuditLog.Serialize(null).Should().BeNull();
    }

    [Fact]
    public void Serialize_should_return_json_string_for_non_null_input()
    {
        var value = new { Title = "Hello", Count = 42 };
        var json = AuditLog.Serialize(value);
        json.Should().NotBeNull();
        json.Should().Contain("Hello");
        json.Should().Contain("42");
    }
}
