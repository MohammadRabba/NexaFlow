using FluentAssertions;
using NexaFlow.Domain.ValueObjects;
using Xunit;

namespace NexaFlow.Domain.Tests.Common;

/// <summary>
///     Tests for the <see cref="TenantId" /> value type — section 6 (Multi-Tenancy)
///     and section 9 (Authorization). The struct makes accidental misuse of an
///     empty Guid as a tenant impossible at the type level.
/// </summary>
public sealed class TenantIdTests
{
    [Fact]
    public void Constructor_should_reject_empty_guid()
    {
        var act = () => new TenantId(Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_should_return_false_for_empty_guid()
    {
        var ok = TenantId.TryFrom(Guid.Empty, out var id);
        ok.Should().BeFalse();
        // Default(TenantId) has Value=Guid.Empty since the constructor was bypassed
        id.Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void From_should_create_valid_tenant_id()
    {
        var id = TenantId.From(Guid.NewGuid());
        id.Value.Should().NotBeEmpty();
    }

    [Fact]
    public void ToGuid_should_provide_explicit_conversion()
    {
        var guid = Guid.NewGuid();
        var id = TenantId.From(guid);
        id.ToGuid().Should().Be(guid);
    }

    [Fact]
    public void Equality_should_be_value_based()
    {
        var guid = Guid.NewGuid();
        var a = TenantId.From(guid);
        var b = TenantId.From(guid);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
