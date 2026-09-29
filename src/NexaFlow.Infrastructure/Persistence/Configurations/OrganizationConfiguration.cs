using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(o => o.Slug).HasColumnName("slug").HasMaxLength(60).IsRequired();
        builder.HasIndex(o => o.Slug).IsUnique();

        builder.Property(o => o.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(o => o.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(o => o.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(o => o.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(o => o.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(o => o.UpdatedByUserId).HasColumnName("updated_by_user_id");

        // Organization IS the tenant root — it does not carry its own OrganizationId.
        // (ITenantEntity is NOT implemented by Organization itself.)
        builder.Ignore(o => o.DomainEvents);

        // Owned collection: OrganizationMember rows owned by the aggregate.
        // Per section 17 — the aggregate's invariant "an organization has at least one Owner"
        // is enforced by the domain factory. EF Core just persists the structure.
        builder.Metadata.FindNavigation(nameof(Organization.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Members)
            .WithOne()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
