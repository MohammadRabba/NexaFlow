using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(t => t.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(t => t.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description").HasMaxLength(5000).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(t => t.Priority).HasColumnName("priority").HasConversion<int>().IsRequired();
        builder.Property(t => t.AssigneeId).HasColumnName("assignee_id");
        builder.Property(t => t.ReporterId).HasColumnName("reporter_id").IsRequired();
        builder.Property(t => t.DueDateUtc).HasColumnName("due_date_utc");
        builder.Property(t => t.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(t => t.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(t => t.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Indexes justified by actual query patterns:
        // - (project_id, status): "list tasks in project X with status Y" — the common filter.
        // - (project_id, priority): "list tasks in project X with priority Y" — secondary filter.
        // - assignee_id: "my tasks" query across all projects.
        // We don't add (project_id) alone — the composite leftmost prefix covers it.
        builder.HasIndex(t => new { t.ProjectId, t.Status })
            .HasDatabaseName("ix_tasks_project_id_status");
        builder.HasIndex(t => new { t.ProjectId, t.Priority })
            .HasDatabaseName("ix_tasks_project_id_priority");
        builder.HasIndex(t => t.AssigneeId).HasDatabaseName("ix_tasks_assignee_id");

        builder.Ignore(t => t.DomainEvents);

        // FKs
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        // AssigneeId is nullable — SET NULL on user delete (we don't hard-delete users, but defensive).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Organization FK (tenant filter + RESTRICT)
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
