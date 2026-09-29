using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction over "which tenant are we acting in for this request / job / signal".
///     <para>
///         Per the approved Phase 1 architecture:
///         <b>The Application layer must NOT be tightly coupled to the X-Organization-Id
///         HTTP header.</b> The transport mechanism can evolve (JWT claim, sub-domain,
///         SignalR query string, message header, scheduled-job context, etc.).
///     </para>
///     <para>
///         The Application layer only knows that there is a current tenant (or that
///         there isn't — e.g., an unauthenticated registration endpoint or a host-level
///         background job). It does not know HOW the tenant id was resolved.
///     </para>
///     <para>
///         Resolution rules (section 7):
///         <list type="bullet">
///             <item>The tenant id comes from a trusted authenticated context — never blindly from a header.</item>
///             <item>If the authenticated user belongs to multiple organizations, the caller MUST supply a candidate and we validate it against their memberships.</item>
///             <item>An empty / missing tenant for a tenant-scoped command = error, not silent default.</item>
///         </list>
///     </para>
/// </summary>
public interface ICurrentTenantService
{
    /// <summary>
    ///     The currently resolved tenant id, or null if no tenant has been set for this
    ///     scope. Tenant-scoped command handlers MUST call <see cref="RequireTenantId" />
    ///     before acting; reading a null here means we should refuse the operation.
    /// </summary>
    TenantId? OrganizationId { get; }

    /// <summary>
    ///     True if a tenant has been resolved for the current scope. False for:
    ///     unauthenticated endpoints (auth/register), host-scoped background jobs,
    ///     or a misconfigured request that hasn't yet been through tenant resolution.
    /// </summary>
    bool IsTenantResolved { get; }

    /// <summary>
    ///     Returns the resolved tenant id, or throws <see cref="InvalidOperationException" />
    ///     if none has been set. The standard way for command handlers to obtain the
    ///     tenant — explicit failure is preferred over silent misattribution.
    /// </summary>
    Guid RequireTenantId();

    /// <summary>
    ///     Ensures the resolved tenant matches <paramref name="expectedOrganizationId" />.
    ///     Throws <see cref="NexaFlow.Domain.Exceptions.NotFoundException" /> if:
    ///     <list type="bullet">
    ///         <item>No tenant is resolved (the user did not select an org, or selected a different one).</item>
    ///         <item>The resolved tenant id differs from the URL's organization id.</item>
    ///     </list>
    ///     Returns the resolved tenant id on success.
    ///     <para>
    ///         This is the cross-tenant leak guard. Use it at the start of every handler
    ///         that takes an organizationId from the URL — the URL id must match the
    ///         resolved tenant, otherwise the request is either a typo or an attack,
    ///         and we return 404 (no enumeration leak).
    ///     </para>
    /// </summary>
    Guid EnsureMatchesTenantId(Guid expectedOrganizationId);
}

/// <summary>
///     The strategy that actually <em>resolves</em> a tenant for the current scope
///     from whatever transport the host is running. Infrastructure implementations:
///     <list type="bullet">
///         <item>HTTP middleware — reads a candidate from JWT claim or X-Organization-Id header, validates against the user's memberships, and pushes it into the scoped <c>CurrentTenantService</c>.</item>
///         <item>Background worker — supplies the tenant id from the job's context.</item>
///         <item>Message consumer — reads a tenant id from message headers.</item>
///         <item>Tests — supplies a fixed tenant id.</item>
///     </list>
///     <para>
///         <b>Security contract</b>: the resolver MUST validate the candidate against
///         the authenticated principal's memberships before returning. A client-supplied
///         OrganizationId is data, not authority.
///     </para>
/// </summary>
public interface ITenantResolutionStrategy
{
    /// <summary>
    ///     Resolve the current tenant for this scope. Returns null if no tenant applies
    ///     (e.g., unauthenticated request). Throws if a candidate was supplied but
    ///     the user is not a member of that organization.
    /// </summary>
    Task<TenantId?> ResolveAsync(CancellationToken cancellationToken = default);
}
