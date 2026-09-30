using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     Test double for <see cref="ICurrentTenantService" />. Allows tests to
///     explicitly set / clear the resolved tenant — useful for testing
///     tenant-scoped handlers, audit log behavior, etc.
/// </summary>
public sealed class FakeCurrentTenantService : ICurrentTenantService
{
    private TenantId? _tenantId;

    public TenantId? OrganizationId => _tenantId;
    public bool IsTenantResolved => _tenantId is not null;

    public Guid RequireTenantId() =>
        _tenantId?.Value ?? throw new InvalidOperationException("No tenant resolved.");

    public Guid EnsureMatchesTenantId(Guid expectedOrganizationId)
    {
        if (_tenantId is null || _tenantId.Value != expectedOrganizationId)
            throw new InvalidOperationException(
                $"Tenant mismatch. Expected {expectedOrganizationId}, resolved {_tenantId?.Value}.");
        return expectedOrganizationId;
    }

    public void SetTenant(TenantId tenant) => _tenantId = tenant;

    public void ClearTenant() => _tenantId = null;
}
