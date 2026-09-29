using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        // Email — value object owned by User. Stored as two columns: value (display) + normalized.
        builder.OwnsOne(u => u.Email, eb =>
        {
            eb.Property(e => e.Value).HasColumnName("email").HasMaxLength(320).IsRequired();
            eb.Property(e => e.Normalized).HasColumnName("email_normalized").HasMaxLength(320).IsRequired();
            eb.HasIndex(e => e.Normalized).IsUnique();
        });

        builder.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(128).IsRequired();

        builder.Property(u => u.EmailVerified).HasColumnName("email_verified").IsRequired();
        builder.Property(u => u.EmailVerifiedAtUtc).HasColumnName("email_verified_at_utc");

        builder.Property(u => u.FailedLoginAttempts).HasColumnName("failed_login_attempts").IsRequired();
        builder.Property(u => u.LockoutEndUtc).HasColumnName("lockout_end_utc");

        // --- Phase 2: Email verification token (hashed, single-use) ---
        builder.Property(u => u.EmailVerificationTokenHash)
            .HasColumnName("email_verification_token_hash")
            .HasMaxLength(128);
        builder.Property(u => u.EmailVerificationTokenExpiresAtUtc)
            .HasColumnName("email_verification_token_expires_at_utc");
        // Index to support token-hash lookups at verification time.
        builder.HasIndex(u => u.EmailVerificationTokenHash)
            .HasDatabaseName("ix_users_email_verification_token_hash");

        // --- Phase 2: Password reset token (hashed, single-use) ---
        builder.Property(u => u.PasswordResetTokenHash)
            .HasColumnName("password_reset_token_hash")
            .HasMaxLength(128);
        builder.Property(u => u.PasswordResetTokenExpiresAtUtc)
            .HasColumnName("password_reset_token_expires_at_utc");
        builder.HasIndex(u => u.PasswordResetTokenHash)
            .HasDatabaseName("ix_users_password_reset_token_hash");

        builder.Property(u => u.PasswordChangedAtUtc).HasColumnName("password_changed_at_utc");

        builder.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(u => u.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(u => u.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(u => u.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // User is NOT tenant-scoped — a user can belong to many organizations via OrganizationMember.
        builder.Ignore(u => u.DomainEvents);
    }
}
