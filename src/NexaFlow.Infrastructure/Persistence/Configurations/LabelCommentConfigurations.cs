using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(l => l.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(l => l.Color).HasColumnName("color").HasMaxLength(20);
        builder.Property(l => l.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(l => l.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(l => l.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(l => l.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Unique per org — no duplicate label names within the same organization.
        builder.HasIndex(l => new { l.OrganizationId, l.Name }).IsUnique()
            .HasDatabaseName("uq_labels_organization_id_name");
        builder.Ignore(l => l.DomainEvents);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(l => l.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaskLabelConfiguration : IEntityTypeConfiguration<TaskLabel>
{
    public void Configure(EntityTypeBuilder<TaskLabel> builder)
    {
        builder.ToTable("task_labels");
        builder.HasKey(tl => tl.Id);
        builder.Property(tl => tl.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(tl => tl.LabelId).HasColumnName("label_id").IsRequired();
        builder.Property(tl => tl.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(tl => tl.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        // Unique — prevents duplicate task-label relationships.
        builder.HasIndex(tl => new { tl.TaskId, tl.LabelId }).IsUnique()
            .HasDatabaseName("uq_task_labels_task_id_label_id");
        builder.Ignore(tl => tl.DomainEvents);

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(tl => tl.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Label>()
            .WithMany()
            .HasForeignKey(tl => tl.LabelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(tl => tl.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(c => c.Body).HasColumnName("body").HasMaxLength(5000).IsRequired();
        builder.Property(c => c.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(c => c.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(c => c.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Index: "list comments for task X" — the primary query pattern.
        builder.HasIndex(c => c.TaskId).HasDatabaseName("ix_comments_task_id");
        builder.Ignore(c => c.DomainEvents);

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
