using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("organization_members");
        builder.HasKey(m => m.Id);

        // OrganizationMember is tenant-scoped (carries OrganizationId and implements ITenantEntity).
        builder.Property(m => m.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.Role).HasColumnName("role").HasConversion<int>().IsRequired();

        // UNIQUE membership: a user can hold exactly one role per organization.
        // Index on OrganizationId also serves the tenant query filter's predicate.
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);

        builder.Property(m => m.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(m => m.InvitationTokenHash).HasColumnName("invitation_token_hash").HasMaxLength(256);
        builder.Property(m => m.AcceptedAtUtc).HasColumnName("accepted_at_utc");

        builder.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(m => m.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(m => m.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(m => m.UpdatedByUserId).HasColumnName("updated_by_user_id");

        builder.Ignore(m => m.DomainEvents);
    }
}
