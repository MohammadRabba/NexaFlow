namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction for atomic persistence — section 14: "Use transactions around
///     operations that require atomic consistency. For example: Create Project +
///     Add Project Member + Create Audit Log should either all succeed or fail together."
///     <para>
///         Phase 1: declared but no callers yet (no transactional commands until Phase 4).
///         Infrastructure provides the EF Core implementation in Phase 1 so DI is wired.
///     </para>
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Begin a unit of work. Calling SaveChangesAsync commits the transaction.</summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commit the unit of work. Throws on any failure; caller is responsible for rollback.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
}
