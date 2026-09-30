using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Audit;

/// <summary>
///     Default implementation of <see cref="IAuditService" />. Adds an
///     <see cref="AuditLog" /> row to the ambient
///     <see cref="IApplicationDbContext" /> so that the next
///     <see cref="IApplicationDbContext.SaveChangesAsync" /> persists both the
///     business mutation AND the audit row in the same transaction (spec §14).
/// </summary>
/// <remarks>
///     <para>
///         <b>Lifetime:</b> scoped. The service reads ambient
///         <see cref="ICurrentUserService" /> / <see cref="ICurrentTenantService" />
///         which are themselves scoped — sharing them across the request is correct
///         and avoids stale tenant / user information.
///     </para>
///     <para>
///         <b>Failure behavior:</b> if audit recording throws (e.g., the AuditLog
///         factory rejects an over-long action string), the exception propagates to
///         the calling handler, which fails the entire transaction. This is intentional:
///         a failure to record audit is treated as a failure of the operation — never
///         silently swallow audit errors. Spec §25: "Be careful with sensitive information"
///         — the implementation does not redact; the caller is responsible.
///     </para>
/// </remarks>
public sealed class AuditService : IAuditService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ILogger<AuditService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public Task RecordAsync(
        string action,
        string entity,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        Guid? actorUserIdOverride = null,
        Guid? organizationIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        // cancellationToken is honored by EF Core's Add/SaveChangesAsync; the factory
        // itself is synchronous. We accept the token for API symmetry — the handler may
        // pass a CT that's already canceled, in which case EF will observe it on SaveChanges.

        var actorUserId = actorUserIdOverride ?? _currentUser.UserId;
        // organizationIdOverride is intentionally nullable: for auth events we want to
        // record "no tenant". A null override means "use ambient if available"; an explicit
        // Guid.Empty override would also be treated as null — but the only caller that
        // passes an override passes null explicitly.
        Guid? organizationId = organizationIdOverride ?? _currentTenant.OrganizationId;
        // Normalize: an empty Guid from a misconfigured tenant service is the same as null.
        if (organizationId == Guid.Empty)
        {
            organizationId = null;
        }

        var ipAddress = _currentUser.IPAddress;
        var now = DateTimeOffset.UtcNow;

        var entry = AuditLog.Create(
            userId: actorUserId,
            organizationId: organizationId,
            action: action,
            entity: entity,
            entityId: entityId,
            oldValues: oldValues,
            newValues: newValues,
            ipAddress: ipAddress,
            atUtc: now);

        _db.Add(entry);

        // Diagnostic log — does NOT contain the audit payload itself (which may include
        // business data). The audit row is persisted via SaveChangesAsync.
        _logger.LogDebug(
            "Recorded audit entry: Action={Action}, Entity={Entity}, EntityId={EntityId}, Actor={ActorUserId}, Tenant={OrganizationId}",
            action, entity, entityId, actorUserId, organizationId);

        return Task.CompletedTask;
    }
}
