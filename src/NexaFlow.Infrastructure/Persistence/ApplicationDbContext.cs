using System.Linq.Expressions;
using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Common;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Infrastructure.Persistence;

/// <summary>
///     EF Core DbContext for NexaFlow. Implements <see cref="IApplicationDbContext" />
///     using <c>IQueryable&lt;T&gt;</c> (BCL) + <c>Add&lt;T&gt;</c> / <c>Remove&lt;T&gt;</c>
///     mutation methods — Application never sees EF Core types.
/// </summary>
public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService? _currentUser;
    private readonly ITenantServiceAccessor _tenantAccessor;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUser,
        ITenantServiceAccessor tenantAccessor)
        : base(options)
    {
        _currentUser = currentUser;
        _tenantAccessor = tenantAccessor;
    }

    // EF Core's DbSet<T> implements IQueryable<T>, so the IApplicationDbContext contract
    // is satisfied by simply exposing the DbSets as IQueryable<T>. Mutation methods
    // delegate to DbContext.Set<T>() internally.

    IQueryable<User> IApplicationDbContext.Users => Users;
    IQueryable<Organization> IApplicationDbContext.Organizations => Organizations;
    IQueryable<OrganizationMember> IApplicationDbContext.OrganizationMembers => OrganizationMembers;
    IQueryable<RefreshToken> IApplicationDbContext.RefreshTokens => RefreshTokens;

    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    void IApplicationDbContext.Add<TEntity>(TEntity entity) where TEntity : class
        => Set<TEntity>().Add(entity);

    void IApplicationDbContext.Remove<TEntity>(TEntity entity) where TEntity : class
        => Set<TEntity>().Remove(entity);

    // --- Named async queries (the "thin repository" extension path per ADR-002) ---

    Task<User?> IApplicationDbContext.FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct)
        => Users.SingleOrDefaultAsync(u => u.Email.Normalized == normalizedEmail, ct);

    Task<User?> IApplicationDbContext.FindUserByIdAsync(Guid id, CancellationToken ct)
        => Users.SingleOrDefaultAsync(u => u.Id == id, ct);

    Task<bool> IApplicationDbContext.EmailIsInUseAsync(string normalizedEmail, CancellationToken ct)
        => Users.AnyAsync(u => u.Email.Normalized == normalizedEmail, ct);

    Task<RefreshToken?> IApplicationDbContext.FindRefreshTokenByHashAsync(string tokenHash, CancellationToken ct)
        => RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    Task<int> IApplicationDbContext.CountActiveRefreshTokensInFamilyAsync(Guid familyId, CancellationToken ct)
        => RefreshTokens.CountAsync(t => t.FamilyId == familyId && t.RevokedAtUtc == null, ct);

    Task<bool> IApplicationDbContext.IsOrganizationSlugTakenAsync(string slug, CancellationToken ct)
        => Organizations.AnyAsync(o => o.Slug == slug, ct);

    Task<Organization?> IApplicationDbContext.FindOrganizationByIdAsync(Guid id, CancellationToken ct)
        => Organizations.SingleOrDefaultAsync(o => o.Id == id, ct);

    Task<Organization?> IApplicationDbContext.FindOrganizationWithMembersAsync(Guid id, CancellationToken ct)
        // Include Members via the navigation; EF Core's identity map ensures the Members collection
        // is consistent with the DbSet. Use IgnoreQueryFilters because the OrganizationMember table
        // is itself tenant-scoped — we want to load ALL members of the requested org, not just those
        // whose OrganizationId matches the ambient tenant (which IS this org, but being explicit).
        => Organizations
            .Include(o => o.Members)
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(o => o.Id == id, ct);

    Task<OrganizationMember?> IApplicationDbContext.FindMembershipAsync(
        Guid organizationId, Guid userId, CancellationToken ct)
        // Bypass the global query filter — we are explicitly querying the membership for a
        // specific (org, user) pair, which may not match the ambient tenant (e.g., during
        // tenant-resolution middleware itself).
        => OrganizationMembers
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId && m.IsActive)
            .FirstOrDefaultAsync(ct);

    async Task<(List<OrganizationMember> Items, long Total)> IApplicationDbContext.GetPagedMembersAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct)
    {
        // The global query filter scopes by the ambient tenant. We pass organizationId
        // explicitly to validate it's the same as the ambient tenant (otherwise the
        // filter returns nothing, which is the correct cross-tenant safe behavior).
        var query = OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.IsActive);
        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderBy(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    async Task<(List<Organization> Items, long Total)> IApplicationDbContext.GetPagedOrganizationsForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken ct)
    {
        // Organizations are not tenant-scoped (they ARE the tenant). The query filter does
        // not apply. We join through OrganizationMember to find orgs this user belongs to.
        var query = from org in Organizations
                    join m in OrganizationMembers on org.Id equals m.OrganizationId
                    where m.UserId == userId && m.IsActive && org.DeletedAtUtc == null
                    orderby org.CreatedAtUtc descending
                    select org;
        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Snake_case naming is applied at the DbContextOptionsBuilder level via
        // UseSnakeCaseNamingConvention() in the Infrastructure DI setup.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ApplyTenantQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    ///     Build a per-entity query filter: WHERE OrganizationId == currentTenant.
    ///     Fail-closed: when no tenant is resolved (host-scope background job,
    ///     unauthenticated endpoint), the filter excludes ALL tenant rows.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var currentTenant = _tenantAccessor.OrganizationId ?? Guid.Empty;
        var organizationIdProperty = nameof(ITenantEntity.OrganizationId);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType)) continue;
            if (entityType.BaseType is not null) continue;

            var param = Expression.Parameter(entityType.ClrType, "e");
            var propertyAccess = Expression.Property(param, organizationIdProperty);
            var constant = Expression.Constant(currentTenant);
            var equality = Expression.Equal(propertyAccess, constant);
            var lambda = Expression.Lambda(equality, param);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var currentUser = _currentUser?.UserId;
        var currentTenant = _tenantAccessor.OrganizationId;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.StampCreated(now, currentUser);
                    if (entry.Entity is ITenantEntity tenantEntity)
                    {
                        if (currentTenant is null || currentTenant == Guid.Empty)
                        {
                            throw new InvalidOperationException(
                                $"Cannot persist {entry.Entity.GetType().Name} without a resolved tenant. " +
                                "Ensure ICurrentTenantService is set for tenant-scoped operations.");
                        }
                        if (tenantEntity.OrganizationId != currentTenant.Value)
                        {
                            throw new InvalidOperationException(
                                "Entity OrganizationId does not match the ambient tenant. " +
                                "Application handlers must construct tenant-owned entities with the " +
                                "current tenant id from ICurrentTenantService.");
                        }
                    }
                    break;

                case EntityState.Modified:
                    entry.Entity.StampUpdated(now, currentUser);
                    if (entry.Entity is ITenantEntity)
                    {
                        var orgIdProperty = entry.Property(nameof(ITenantEntity.OrganizationId));
                        if (orgIdProperty.IsModified &&
                            !Equals(orgIdProperty.OriginalValue, orgIdProperty.CurrentValue))
                        {
                            throw new InvalidOperationException(
                                "OrganizationId is immutable. Tenant re-assignment is not allowed.");
                        }
                    }
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
///     Internal accessor used by the DbContext to read the resolved tenant for the global
///     query filter. Wraps <c>ICurrentTenantService</c> to keep the dependency explicit and
///     to allow a host-scope job (no tenant) to be expressed as null rather than exception.
/// </summary>
public interface ITenantServiceAccessor
{
    Guid? OrganizationId { get; }
}
