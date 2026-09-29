using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        // RefreshToken is user-scoped, NOT tenant-scoped. A refresh token grants access to all
        // organizations the user belongs to — no OrganizationId column here.
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();

        // Unique index on the token hash — the lookup key at rotation / revocation time.
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("uq_refresh_tokens_token_hash");

        // Index on user_id for "revoke all for user" queries.
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_refresh_tokens_user_id");

        // Composite index on (family_id, revoked_at_utc) for family-wide revocation queries.
        // The query "WHERE family_id = X AND revoked_at_utc IS NULL" is supported by this index.
        builder.HasIndex(t => new { t.FamilyId, t.RevokedAtUtc })
            .HasDatabaseName("ix_refresh_tokens_family_revoked");

        // Index on expiry for the cleanup worker (Phase 6) that purges expired tokens.
        builder.HasIndex(t => t.ExpiresAtUtc).HasDatabaseName("ix_refresh_tokens_expires_at");

        builder.Property(t => t.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(t => t.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(t => t.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(t => t.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        builder.Property(t => t.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(64);
        builder.Property(t => t.CreatedFromIp).HasColumnName("created_from_ip").HasMaxLength(64);
        builder.Property(t => t.CreatedByUserAgent).HasColumnName("created_by_user_agent").HasMaxLength(512);
        builder.Property(t => t.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(t => t.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(t => t.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Self-referencing FK: a refresh token may be "replaced by" another refresh token in the same family.
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(t => t.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.Ignore(t => t.DomainEvents);
    }
}
