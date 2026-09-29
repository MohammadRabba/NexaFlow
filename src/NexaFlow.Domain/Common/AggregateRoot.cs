namespace NexaFlow.Domain.Common;

/// <summary>
///     Base class for aggregate roots. Adds soft-delete support and explicit
///     aggregate invariants. Aggregate roots are the only entities that Application
///     layer commands should load / mutate directly (section 17 — Domain Business Rules).
/// </summary>
public abstract class AggregateRoot : AuditableEntity
{
    /// <summary>
    ///     UTC timestamp when the aggregate was soft-deleted, or null if alive.
    ///     Soft-deleted entities remain in the database for audit / referential integrity
    ///     (e.g., comments — section 18). The global query filter scopes them out by default.
    /// </summary>
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    /// <summary>
    ///     Mark this aggregate as deleted without physically removing the row.
    ///     Concrete aggregates may override to add domain events (e.g., <c>TaskDeleted</c>).
    /// </summary>
    public virtual void SoftDelete(Guid? deletedByUserId, DateTimeOffset deletedAtUtc)
    {
        if (IsDeleted) return; // Idempotent

        DeletedAtUtc = deletedAtUtc;
        UpdatedAtUtc = deletedAtUtc;
        UpdatedByUserId = deletedByUserId;
    }

    /// <summary>Restore a previously soft-deleted aggregate. Domain rules decide whether this is allowed.</summary>
    public virtual void Restore(DateTimeOffset restoredAtUtc, Guid? restoredByUserId)
    {
        if (!IsDeleted) return;

        DeletedAtUtc = null;
        UpdatedAtUtc = restoredAtUtc;
        UpdatedByUserId = restoredByUserId;
    }
}
