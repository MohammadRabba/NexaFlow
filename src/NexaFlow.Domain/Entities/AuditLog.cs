using System.Text.Json;
using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     An immutable audit-log row recording a security- or business-sensitive operation
///     (spec §25). Persisted in the SAME transaction as the business mutation so that
///     either both succeed or both fail (spec §14 transactional example explicitly
///     lists "Create Audit Log" alongside "Create Project + Add Project Member").
/// </summary>
/// <remarks>
///     <para>
///         <b>Fields per spec §25:</b> UserId, OrganizationId, Action, Entity, EntityId,
///         OldValues, NewValues, IPAddress, Timestamp.
///     </para>
///     <para>
///         <b>OrganizationId is nullable.</b> Authentication events (LoginSucceeded,
///         LoginFailed, PasswordChanged, EmailVerified) occur BEFORE the user has selected
///         an organization, so there is no ambient tenant at audit time. All other events
///         (TaskCreated, MemberInvited, etc.) occur inside a tenant-scoped handler and
///         carry the resolved <c>OrganizationId</c>.
///     </para>
///     <para>
///         <b>Not <see cref="ITenantEntity" />.</b> Audit rows intentionally span tenant
///         boundaries (auth events have no tenant), so the global query filter does not
///         apply. The Application-layer query method filters explicitly by
///         <c>OrganizationId</c> for tenant-scoped reads.
///     </para>
///     <para>
///         <b>No soft delete.</b> Audit rows are immutable history — never physically
///         or logically deleted. The class therefore inherits <see cref="AuditableEntity" />
///         (for timestamps) but NOT <see cref="AggregateRoot" /> (no soft-delete option).
///     </para>
///     <para>
///         <b>Secrets policy (spec §25):</b> never place passwords, refresh tokens, JWTs,
///         or other authentication secrets in <see cref="OldValues" /> / <see cref="NewValues" />.
///         Callers are responsible for redacting before serializing.
///     </para>
/// </remarks>
public class AuditLog : AuditableEntity
{
    private AuditLog() { } // EF Core

    /// <summary>
    ///     The user who performed the action. Nullable for system / pre-authentication
    ///     events (e.g., a login attempt for a non-existent email — there is no real user).
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    ///     The organization the action affected, or null for auth events that occur
    ///     before tenant resolution (login, password change, email verification).
    /// </summary>
    public Guid? OrganizationId { get; private set; }

    /// <summary>
    ///     Canonical action name. See <see cref="AuditAction" /> for the full set.
    ///     Stored as a string (not an enum) so that historical rows remain readable
    ///     even if action names evolve.
    /// </summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>
    ///     The entity type name affected by the action (e.g., "Task", "Project",
    ///     "OrganizationMember"). Free-form string, not a foreign key — audit rows
    ///     must survive the deletion of the referenced entity.
    /// </summary>
    public string Entity { get; private set; } = string.Empty;

    /// <summary>
    ///     The id of the affected entity, or null for actions that do not target a
    ///     specific entity (e.g., login).
    /// </summary>
    public Guid? EntityId { get; private set; }

    /// <summary>
    ///     JSON-serialized snapshot of the affected entity's relevant fields BEFORE the
    ///     mutation. Null for create / login / logout events. MUST NOT contain secrets.
    /// </summary>
    public string? OldValues { get; private set; }

    /// <summary>
    ///     JSON-serialized snapshot of the affected entity's relevant fields AFTER the
    ///     mutation. Null for delete events. MUST NOT contain secrets.
    /// </summary>
    public string? NewValues { get; private set; }

    /// <summary>
    ///     The caller's IP address at the time of the action, or null in non-HTTP
    ///     contexts (background workers, tests).
    /// </summary>
    public string? IPAddress { get; private set; }

    /// <summary>
    ///     UTC timestamp at which the action was recorded. Set by the factory; the
    ///     Infrastructure SaveChangesAsync also stamps CreatedAtUtc, which is the
    ///     canonical "when the row was persisted" timestamp. The two are equal in
    ///     practice (the factory timestamp is taken in the same unit of work).
    /// </summary>
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>
    ///     Factory. Validates the basic shape of an audit row. The caller is responsible
    ///     for redacting secrets from <paramref name="oldValues" /> / <paramref name="newValues" />
    ///     BEFORE calling this method.
    /// </summary>
    public static AuditLog Create(
        Guid? userId,
        Guid? organizationId,
        string action,
        string entity,
        Guid? entityId,
        string? oldValues,
        string? newValues,
        string? ipAddress,
        DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        if (action.Length > 64)
            throw new ArgumentException("Action must not exceed 64 characters.", nameof(action));
        if (entity.Length > 64)
            throw new ArgumentException("Entity must not exceed 64 characters.", nameof(entity));
        if (ipAddress is { Length: > 45 })
            // IPv6 is the upper bound at 45 chars; anything longer is suspicious.
            throw new ArgumentException("IPAddress must not exceed 45 characters.", nameof(ipAddress));

        return new AuditLog
        {
            UserId = userId,
            OrganizationId = organizationId,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            IPAddress = ipAddress,
            Timestamp = atUtc,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    /// <summary>
    ///     Convenience: serialize a value to JSON for <see cref="OldValues" /> /
    ///     <see cref="NewValues" />. Returns null when the input is null. The caller
    ///     is still responsible for redacting secrets BEFORE calling this helper —
    ///     the helper does not know which fields are sensitive.
    /// </summary>
    public static string? Serialize(object? value)
        => value is null ? null : JsonSerializer.Serialize(value);
}
