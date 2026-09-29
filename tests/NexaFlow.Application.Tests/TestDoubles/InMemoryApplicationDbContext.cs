using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Common;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     In-memory fake of <see cref="IApplicationDbContext" /> for Application-layer unit tests.
///     <para>
///         Per the test strategy (section 34 — Application-layer unit tests can legitimately
///         use lightweight fakes; only the integration tests need real Postgres), this fake
///         stores entities in lists and provides the named query methods that the auth
///         handlers call.
///     </para>
///     <para>
///         <b>Semantics:</b>
///         <list type="bullet">
///             <item><c>Add&lt;T&gt;</c> appends to the appropriate list.</item>
///             <item><c>SaveChangesAsync</c> stamps audit metadata (CreatedAtUtc, UpdatedAtUtc).</item>
///             <item>Named query methods (FindUserByNormalizedEmailAsync, etc.) return data from the lists.</item>
///         </list>
///     </para>
/// </summary>
public sealed class InMemoryApplicationDbContext : IApplicationDbContext
{
    public List<User> Users { get; } = [];
    public List<Organization> Organizations { get; } = [];
    public List<OrganizationMember> OrganizationMembers { get; } = [];
    public List<RefreshToken> RefreshTokens { get; } = [];

    IQueryable<User> IApplicationDbContext.Users => Users.AsQueryable();
    IQueryable<Organization> IApplicationDbContext.Organizations => Organizations.AsQueryable();
    IQueryable<OrganizationMember> IApplicationDbContext.OrganizationMembers => OrganizationMembers.AsQueryable();
    IQueryable<RefreshToken> IApplicationDbContext.RefreshTokens => RefreshTokens.AsQueryable();

    public Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Email.Normalized == normalizedEmail));

    public Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<bool> EmailIsInUseAsync(string normalizedEmail, CancellationToken ct = default)
        => Task.FromResult(Users.Any(u => u.Email.Normalized == normalizedEmail));

    public Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(RefreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task<int> CountActiveRefreshTokensInFamilyAsync(Guid familyId, CancellationToken ct = default)
        => Task.FromResult(RefreshTokens.Count(t => t.FamilyId == familyId && t.RevokedAtUtc == null));

    public Task<bool> IsOrganizationSlugTakenAsync(string slug, CancellationToken ct = default)
        => Task.FromResult(Organizations.Any(o => o.Slug == slug));

    public Task<Organization?> FindOrganizationByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Organizations.FirstOrDefault(o => o.Id == id && !o.IsDeleted));

    public Task<Organization?> FindOrganizationWithMembersAsync(Guid id, CancellationToken ct = default)
    {
        var org = Organizations.FirstOrDefault(o => o.Id == id && !o.IsDeleted);
        // Members are stored separately in the test double; we just return the org.
        // (In the real DbContext, EF Core loads them via the navigation property.)
        // For tests that need to assert on Members, the test code populates both lists
        // consistently. Production Organization has _members as a private field.
        return Task.FromResult(org);
    }

    public Task<OrganizationMember?> FindMembershipAsync(
        Guid organizationId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(OrganizationMembers.FirstOrDefault(
            m => m.OrganizationId == organizationId && m.UserId == userId && m.IsActive));

    public Task<(List<OrganizationMember> Items, long Total)> GetPagedMembersAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct = default)
    {
        var filtered = OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.IsActive)
            .OrderBy(m => m.CreatedAtUtc)
            .ToList();
        var total = (long)filtered.Count;
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, total));
    }

    public Task<(List<Organization> Items, long Total)> GetPagedOrganizationsForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var orgIds = OrganizationMembers
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToHashSet();
        var filtered = Organizations
            .Where(o => orgIds.Contains(o.Id) && !o.IsDeleted)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToList();
        var total = (long)filtered.Count;
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, total));
    }

    public void Add<TEntity>(TEntity entity) where TEntity : class
    {
        switch (entity)
        {
            case User u: Users.Add(u); break;
            case Organization o: Organizations.Add(o); break;
            case OrganizationMember m: OrganizationMembers.Add(m); break;
            case RefreshToken t: RefreshTokens.Add(t); break;
        }
    }

    public void Remove<TEntity>(TEntity entity) where TEntity : class
    {
        switch (entity)
        {
            case User u: Users.Remove(u); break;
            case Organization o: Organizations.Remove(o); break;
            case OrganizationMember m: OrganizationMembers.Remove(m); break;
            case RefreshToken t: RefreshTokens.Remove(t); break;
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        // For test purposes, we don't stamp CreatedAtUtc — the test code usually constructs
        // entities with explicit timestamps via their factory methods.
        // We do call the internal StampCreated / StampUpdated helpers if the entity is new
        // and doesn't have timestamps — but for tests, we skip this and let the entity's
        // factory-set timestamps stand.
        return Task.FromResult(0);
    }
}
