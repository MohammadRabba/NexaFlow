using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.EventType).HasColumnName("event_type").HasMaxLength(256).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("text").IsRequired();
        builder.Property(m => m.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(m => m.ProcessedOnUtc).HasColumnName("processed_on_utc");
        builder.Property(m => m.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(m => m.Error).HasColumnName("error").HasMaxLength(2000);

        // Index for the OutboxProcessor's polling query:
        // SELECT ... WHERE processed_on_utc IS NULL ORDER BY occurred_on_utc LIMIT N
        builder.HasIndex(m => m.ProcessedOnUtc)
            .HasDatabaseName("ix_outbox_messages_processed_on_utc");
    }
}
