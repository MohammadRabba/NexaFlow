using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.ValueObjects;
using NexaFlow.Infrastructure.Persistence;

namespace NexaFlow.Infrastructure.Services;

/// <summary>
///     Scoped implementation of <see cref="ICurrentTenantService" />.
///     <para>
///         Phase 1 scope: holds a single tenant id set per request by the
///         <c>ITenantResolutionStrategy</c>. The strategy implementation (HTTP middleware
///         reading JWT claims + X-Organization-Id header) lands in Phase 2 with Auth;
///         the strategy here in Phase 1 is the test / host-scope fallback that reads
///         from a per-scope AsyncLocal slot.
///     </para>
///     <para>
///         Implements <see cref="ITenantServiceAccessor" /> too, so the DbContext can
///         read the same resolved tenant for its global query filter — single source of truth.
///     </para>
/// </summary>
public sealed class CurrentTenantService : ICurrentTenantService, ITenantServiceAccessor
{
    private TenantId? _tenantId;

    /// <inheritdoc />
    public TenantId? OrganizationId => _tenantId;

    /// <inheritdoc />
    public bool IsTenantResolved => _tenantId is not null;

    /// <inheritdoc />
    public Guid RequireTenantId()
    {
        if (_tenantId is { } tenant)
        {
            return tenant.ToGuid();
        }
        throw new InvalidOperationException(
            "No tenant is resolved for the current scope. " +
            "Ensure tenant resolution has run before tenant-scoped operations.");
    }

    /// <summary>
    ///     Set the resolved tenant. Called by <c>ITenantResolutionStrategy</c> after
    ///     validating that the authenticated user is a member of the requested organization.
    ///     Public so a background worker or test can set the tenant explicitly.
    /// </summary>
    internal void SetTenant(TenantId tenant)
    {
        _tenantId = tenant;
    }

    /// <summary>Reset the tenant. Called by middleware at end-of-scope (defensive).</summary>
    internal void ClearTenant()
    {
        _tenantId = null;
    }

    Guid? ITenantServiceAccessor.OrganizationId => _tenantId?.ToGuid();
}
