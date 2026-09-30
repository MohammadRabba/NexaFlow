using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

/// <summary>
///     EF Core configuration for <see cref="AuditLog" />.
/// </summary>
/// <remarks>
///     <para>
///         Table layout follows the spec §25 record fields. Indexes target the
///         query patterns of <c>GetPagedAuditLogsAsync</c>:
///         <list type="bullet">
///             <item>tenant-scoped chronologically-descending list (organization_id, occurred_at_utc)</item>
///             <item>per-user audit history (user_id, occurred_at_utc)</item>
///             <item>per-action filtering (action, occurred_at_utc)</item>
///             <item>per-entity history (entity_type, entity_id, occurred_at_utc)</item>
///         </list>
///     </para>
///     <para>
///         AuditLog is intentionally NOT marked <c>ITenantEntity</c> in the domain —
///         auth events carry <c>organization_id = NULL</c>. The query filter is therefore
///         NOT applied automatically; the application layer filters explicitly.
///     </para>
/// </remarks>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.OrganizationId).HasColumnName("organization_id");
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(a => a.Entity).HasColumnName("entity").HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id");
        builder.Property(a => a.OldValues).HasColumnName("old_values").HasColumnType("text");
        builder.Property(a => a.NewValues).HasColumnName("new_values").HasColumnType("text");
        builder.Property(a => a.IPAddress).HasColumnName("ip_address").HasMaxLength(45);
        builder.Property(a => a.Timestamp).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(a => a.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(a => a.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(a => a.UpdatedByUserId).HasColumnName("updated_by_user_id");

        builder.Ignore(a => a.DomainEvents);

        // Primary query: "list the most recent audit events for this organization".
        // Partial index — only rows where organization_id IS NOT NULL are included,
        // which matches the tenant-scoped query path. Auth events (organization_id IS NULL)
        // are queried via the user_id index below.
        builder.HasIndex(a => new { a.OrganizationId, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_organization_id_occurred_at_utc");

        // Per-user audit history (covers auth events: login / logout / password changes).
        builder.HasIndex(a => new { a.UserId, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_user_id_occurred_at_utc");

        // Per-action filtering (e.g., "all MemberInvited events for this org").
        // Composite includes organization_id so the planner can use it for tenant
        // scoping when both filters are present.
        builder.HasIndex(a => new { a.OrganizationId, a.Action, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_organization_id_action_occurred_at_utc");

        // Per-entity history (e.g., "all events for project X"). Composite includes
        // entity_type so the planner can use it as a prefix.
        builder.HasIndex(a => new { a.Entity, a.EntityId, a.Timestamp })
            .HasDatabaseName("ix_audit_logs_entity_entity_id_occurred_at_utc");

        // No foreign keys — audit rows MUST survive the deletion of the referenced
        // user / organization / entity. The references are historical snapshots, not
        // live constraints. (Spec §25: audit logging records security/business-sensitive
        // operations; deleting the user must not erase their audit history.)
    }
}
