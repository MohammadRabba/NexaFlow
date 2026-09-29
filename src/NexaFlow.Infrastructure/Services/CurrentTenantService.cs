using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Exceptions;
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

    /// <inheritdoc />
    public Guid EnsureMatchesTenantId(Guid expectedOrganizationId)
    {
        if (expectedOrganizationId == Guid.Empty)
            throw new ArgumentException("Expected organization id must not be empty.", nameof(expectedOrganizationId));

        if (_tenantId is not { } tenant)
        {
            // No tenant resolved — the user didn't supply X-Organization-Id, or the header
            // was for a different org. From the caller's perspective, this looks like
            // "the resource you're trying to access doesn't exist" (no enumeration leak).
            throw new NotFoundException("Organization", expectedOrganizationId);
        }

        var resolvedId = tenant.ToGuid();
        if (resolvedId != expectedOrganizationId)
        {
            // Cross-tenant leak attempt: the URL points to org B but the resolved tenant is org A.
            // Return 404 — same response as "not found" to avoid confirming org B exists.
            throw new NotFoundException("Organization", expectedOrganizationId);
        }
        return resolvedId;
    }

    /// <summary>
    ///     Set the resolved tenant. Called by <c>ITenantResolutionStrategy</c> after
    ///     validating that the authenticated user is a member of the requested organization.
    ///     Internal, exposed to Api/Tests via InternalsVisibleTo or test can set the tenant explicitly.
    /// </summary>
    internal void SetTenant(TenantId tenant)
    {
        _tenantId = tenant;
    }

    /// <summary>Reset the tenant. Called by middleware at end-of-scope (defensive).</summary>

    Guid? ITenantServiceAccessor.OrganizationId => _tenantId?.ToGuid();
}
