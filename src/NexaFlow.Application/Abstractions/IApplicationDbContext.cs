using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction over the persistence layer. Per the architectural correction
///     applied at the start of Phase 2, Application does NOT reference EF Core.
///     <para>
///         The contract exposes:
///         <list type="bullet">
///             <item><see cref="IQueryable{T}" /> for read-side LINQ composition (BCL — no EF Core).</item>
///             <item>Named async query methods for the queries that justify a "thin repository"
///                 method (Phase 2's auth queries: user by email, user by id, refresh token by hash).
///                 These methods are implemented in Infrastructure with EF Core's async extensions
///                 internally; the contract itself stays EF Core-free.</item>
///             <item><see cref="Add{T}" /> / <see cref="Remove{T}" /> mutation methods.</item>
///         </list>
///     </para>
///     <para>
///         Trade-off (documented in ADR-002): we lose direct access to EF Core's async
///         LINQ extension methods (<c>SingleOrDefaultAsync</c>, <c>AnyAsync</c>,
///         <c>ToListAsync</c>) in Application. Instead, simple lookups get a named method
///         on this interface; complex multi-include queries (when they appear in Phase 4+)
///         will also get named methods returning DTOs.
///     </para>
/// </summary>
public interface IApplicationDbContext
{
    // --- Read-side: typed queryables per aggregate root ---
    // Sync LINQ composition (.Where, .Select, .Any, .Single) is allowed for synchronous
    // iteration patterns. Async iteration MUST use the named query methods below —
    // IQueryable<T>.SingleOrDefaultAsync() etc. are EF Core extensions, not BCL.
    IQueryable<User> Users { get; }
    IQueryable<Organization> Organizations { get; }
    IQueryable<OrganizationMember> OrganizationMembers { get; }
    IQueryable<RefreshToken> RefreshTokens { get; }

    // --- Named async queries (thin repository pattern) ---

    /// <summary>Find a user by their normalized email address.</summary>
    Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>Find a user by id.</summary>
    Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True if any user has the given normalized email.</summary>
    Task<bool> EmailIsInUseAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>Find a refresh token by the SHA-256 hash of the plaintext token.</summary>
    Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Count active refresh tokens in a family.</summary>
    Task<int> CountActiveRefreshTokensInFamilyAsync(Guid familyId, CancellationToken ct = default);

    /// <summary>True if an organization with the given slug exists (case-sensitive).</summary>
    Task<bool> IsOrganizationSlugTakenAsync(string slug, CancellationToken ct = default);

    /// <summary>Find an organization by id (without loading Members). Returns null if not found.</summary>
    Task<Organization?> FindOrganizationByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    ///     Load an organization with all its active members. Used for mutations that need
    ///     to enforce cross-member invariants (e.g., ownership transfer).
    /// </summary>
    Task<Organization?> FindOrganizationWithMembersAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    ///     Find a single membership by (organizationId, userId). Returns null if not found
    ///     or if the membership is inactive. Bypasses the global tenant filter.
    /// </summary>
    Task<OrganizationMember?> FindMembershipAsync(Guid organizationId, Guid userId, CancellationToken ct = default);

    /// <summary>Page the active memberships of an organization. Returns total + items.</summary>
    Task<(List<OrganizationMember> Items, long Total)> GetPagedMembersAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Page the organizations the user belongs to. Returns total + items.</summary>
    Task<(List<Organization> Items, long Total)> GetPagedOrganizationsForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default);

    // --- Mutations: stage changes; SaveChangesAsync commits them ---

    /// <summary>
    ///     Begin tracking <paramref name="entity" /> for insertion. Persisted on the next
    ///     <see cref="SaveChangesAsync" /> call.
    /// </summary>
    void Add<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    ///     Begin tracking <paramref name="entity" /> for deletion. Persisted on the next
    ///     <see cref="SaveChangesAsync" /> call. For aggregates that implement
    ///     <see cref="Domain.Common.AggregateRoot" />, prefer soft-deletion via domain methods.
    /// </summary>
    void Remove<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    ///     Persist pending changes. The Infrastructure implementation is responsible for:
    ///     <list type="bullet">
    ///         <item>Stamping audit metadata (CreatedAt / UpdatedAt / CreatedBy / UpdatedBy).</item>
    ///         <item>Verifying tenant invariants on ITenantEntity rows.</item>
    ///         <item>(Phase 6) dispatching domain events queued on aggregates.</item>
    ///         <item>(Phase 8) writing audit log entries.</item>
    ///     </list>
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
