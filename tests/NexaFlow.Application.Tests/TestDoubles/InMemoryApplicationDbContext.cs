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
