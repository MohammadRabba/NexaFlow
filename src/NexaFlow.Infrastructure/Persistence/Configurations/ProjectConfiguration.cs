using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        // Tenant discriminator. NOT a FK to organizations — the project carries
        // the org id for the global query filter, but the FK is declared via
        // HasOne().WithMany() to be explicit about delete behavior.
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(p => p.StartDateUtc).HasColumnName("start_date_utc");
        builder.Property(p => p.DueDateUtc).HasColumnName("due_date_utc");
        builder.Property(p => p.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(p => p.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(p => p.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(p => p.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Indexes — justified by actual query patterns:
        // - (organization_id, status): the common "list projects in org X with status Y" filter.
        //   Also serves "list all projects in org X" (left-prefix).
        // We don't add a separate index on (organization_id) alone because the composite above
        // covers it (leftmost prefix), and the global query filter on OrganizationId is satisfied
        // by the composite too.
        builder.HasIndex(p => new { p.OrganizationId, p.Status })
            .HasDatabaseName("ix_projects_organization_id_status");

        builder.Ignore(p => p.DomainEvents);

        // FK to organizations — RESTRICT delete (cannot hard-delete an org with projects).
        // Organizations are soft-deleted via AggregateRoot.SoftDelete which sets DeletedAtUtc
        // and does NOT trigger this FK.
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation: Project.Members — used by FindProjectWithMembersAsync to eager-load.
        // The FK on ProjectMember points back to Project; the navigation is read-only here.
        builder.Metadata.FindNavigation(nameof(Project.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Members)
            .WithOne()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade); // when a project is hard-deleted, its memberships go
    }
}
