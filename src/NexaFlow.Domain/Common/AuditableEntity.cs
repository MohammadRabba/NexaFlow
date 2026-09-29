namespace NexaFlow.Domain.Common;

/// <summary>
///     Base class for entities that own audit metadata:
///     CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId.
///     <para>
///         The values are stamped by the Infrastructure layer inside
///         <c>ApplicationDbContext.SaveChangesAsync</c> using the ambient
///         <c>ICurrentUserService</c>. The domain never reads the wall-clock directly
///         (it cannot — it has no infrastructure dependencies).
///     </para>
///     <para>
///         The setters are <c>internal</c> and accessible to the Infrastructure layer
///         only (via <c>InternalsVisibleTo</c>) — they cannot be set from Application or Api.
///     </para>
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAtUtc { get; internal set; }
    public DateTimeOffset UpdatedAtUtc { get; internal set; }
    public Guid? CreatedByUserId { get; internal set; }
    public Guid? UpdatedByUserId { get; internal set; }

    /// <summary>
    ///     Called by <c>ApplicationDbContext</c> when the entity is being inserted.
    ///     Infrastructure-only — domain code never stamps audit metadata.
    /// </summary>
    internal void StampCreated(DateTimeOffset atUtc, Guid? byUserId)
    {
        CreatedAtUtc = atUtc;
        UpdatedAtUtc = atUtc;
        CreatedByUserId = byUserId;
        UpdatedByUserId = byUserId;
    }

    /// <summary>
    ///     Called by <c>ApplicationDbContext</c> when the entity is being modified.
    ///     Infrastructure-only — domain code never stamps audit metadata.
    /// </summary>
    internal void StampUpdated(DateTimeOffset atUtc, Guid? byUserId)
    {
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = byUserId;
    }
}
