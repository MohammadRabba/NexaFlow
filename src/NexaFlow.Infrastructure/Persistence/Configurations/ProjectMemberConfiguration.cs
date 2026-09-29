using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.HasKey(m => m.Id);

        // ProjectMember is itself tenant-scoped (carries OrganizationId, mirrors the
        // project's tenant). The global query filter applies to it too.
        builder.Property(m => m.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(m => m.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.Role).HasColumnName("role").HasConversion<int>().IsRequired();

        builder.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(m => m.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(m => m.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(m => m.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Unique constraint: a user holds exactly one role per project.
        builder.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique()
            .HasDatabaseName("uq_project_members_project_id_user_id");

        // Index: "list this user's projects" — queries join project_members → projects by user_id.
        builder.HasIndex(m => m.UserId).HasDatabaseName("ix_project_members_user_id");

        builder.Ignore(m => m.DomainEvents);

        // FK to projects — CASCADE delete (when a project is hard-deleted, its memberships go).
        // (This is redundant with the navigation's OnDelete above, but explicit.)
        builder.HasOne<Project>()
            .WithMany(p => p.Members)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to organizations — RESTRICT (cannot hard-delete an org with project_members rows).
        // Organizations are soft-deleted only.
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
