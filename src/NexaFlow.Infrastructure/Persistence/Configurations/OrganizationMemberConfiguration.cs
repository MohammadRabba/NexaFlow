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

        // Optimistic concurrency: DEFERRED for Phase 3 (see ADR-006 §"Concurrency").
        //
        // Concurrent cases for Phase 3:
        //   - Duplicate invites: protected by the (organization_id, user_id) unique constraint.
        //   - Concurrent removes: the second remover hits the domain-level "not a member" guard.
        //   - Concurrent role changes / ownership transfers: last-write-wins; the domain
        //     invariant "exactly one Owner" is preserved, but admin A's intent is lost
        //     silently.
        //
        // The Npgsql "xmin-as-concurrency-token" pattern is documented at
        // https://www.npgsql.org/efcore/modeling/concurrency.html — but the migration
        // generator emits an AddColumn("xmin", ...) that we cannot verify applies
        // cleanly against a real PostgreSQL instance in this sandbox (no Docker).
        // Rather than ship an unverified migration, we defer the token. When Docker
        // is available in CI, we'll verify the migration applies and re-add the
        // configuration. The runtime EF Core behavior (xmin in UPDATE WHERE) is
        // documented Npgsql behavior.
    }
}
