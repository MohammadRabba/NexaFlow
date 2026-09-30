using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction for transactional audit-log recording (spec §25, §14).
/// </summary>
/// <remarks>
///     <para>
///         <b>Transactional guarantee:</b> implementations MUST add the <see cref="AuditLog" />
///         row to the same <see cref="IApplicationDbContext" /> used by the calling handler,
///         so that a subsequent <c>SaveChangesAsync</c> persists both the business mutation
///         and the audit row atomically. The spec §14 example explicitly lists "Create
///         Audit Log" alongside "Create Project + Add Project Member" — they share a
///         transaction boundary.
///     </para>
///     <para>
///         <b>Secrets policy (spec §25):</b> the caller is responsible for redacting
///         secrets from <c>oldValues</c> / <c>newValues</c> BEFORE passing them in.
///         Implementations will not redact — they cannot know which fields are sensitive
///         for any given entity.
///     </para>
///     <para>
///         <b>Ambient context:</b> implementations read <c>UserId</c> from
///         <see cref="ICurrentUserService" /> and <c>OrganizationId</c> from
///         <see cref="ICurrentTenantService" /> by default. For auth events that occur
///         BEFORE the ambient context is populated (login, email verification), the
///         caller passes explicit <c>actorUserIdOverride</c> / <c>organizationIdOverride</c>
///         arguments.
///     </para>
/// </remarks>
public interface IAuditService
{
    /// <summary>
    ///     Queue an audit-log row to be persisted in the SAME SaveChangesAsync call as
    ///     the business mutation. Returns synchronously; the row is added via
    ///     <see cref="IApplicationDbContext.Add{TEntity}" /> and is committed when the
    ///     handler calls <see cref="IApplicationDbContext.SaveChangesAsync" />.
    /// </summary>
    /// <param name="action">One of <see cref="AuditAction" />.</param>
    /// <param name="entity">The affected entity type name (e.g., "Task", "Project").</param>
    /// <param name="entityId">The id of the affected entity, or null for actions that target no specific entity.</param>
    /// <param name="oldValues">JSON snapshot BEFORE the mutation, or null. MUST NOT contain secrets.</param>
    /// <param name="newValues">JSON snapshot AFTER the mutation, or null. MUST NOT contain secrets.</param>
    /// <param name="actorUserIdOverride">
    ///     Override the actor's user id. Use for auth events (login, email verification)
    ///     where the ambient <see cref="ICurrentUserService" /> is not yet populated, or
    ///     to record an action performed BY a different user than the ambient one
    ///     (e.g., ownership transfer — the actor is the previous owner, but the ambient
    ///     user is the new owner in some flows).
    /// </param>
    /// <param name="organizationIdOverride">
    ///     Override the tenant. Use for auth events (login) where no tenant is resolved
    ///     yet — pass <c>null</c> explicitly. For tenant-scoped actions, leave this null
    ///     to use the ambient tenant.
    /// </param>
    /// <param name="cancellationToken">Standard cancellation token.</param>
    Task RecordAsync(
        string action,
        string entity,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        Guid? actorUserIdOverride = null,
        Guid? organizationIdOverride = null,
        CancellationToken cancellationToken = default);
}
