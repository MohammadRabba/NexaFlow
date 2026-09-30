using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(n => n.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(n => n.NotificationType).HasColumnName("notification_type").HasMaxLength(64).IsRequired();
        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasColumnName("message").HasMaxLength(1000);
        builder.Property(n => n.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(n => n.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(64);
        builder.Property(n => n.IsRead).HasColumnName("is_read").IsRequired();
        builder.Property(n => n.ReadAtUtc).HasColumnName("read_at_utc");
        builder.Property(n => n.SourceEventId).HasColumnName("source_event_id");

        // Unique constraint on SourceEventId — prevents duplicate notifications from
        // the same domain event under at-least-once delivery (section 19/22).
        builder.HasIndex(n => n.SourceEventId)
            .IsUnique()
            .HasDatabaseName("uq_notifications_source_event_id");
        builder.Property(n => n.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(n => n.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(n => n.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(n => n.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(n => n.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Index: "list this user's unread notifications" — the common query.
        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead })
            .HasDatabaseName("ix_notifications_recipient_user_id_is_read");

        builder.Ignore(n => n.DomainEvents);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(n => n.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
