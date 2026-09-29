using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NexaFlow.Application.Abstractions;
using NexaFlow.Infrastructure.Persistence;

namespace NexaFlow.Infrastructure.Services;

/// <summary>
///     EF Core-backed unit of work. Wraps <c>BeginTransaction</c> / <c>Commit</c> over
///     the ambient <c>ApplicationDbContext</c>. Section 14: used only where atomic
///     consistency is required (e.g., Create Project + Add Member + Audit Log).
///     <para>
///         Phase 1: registered so DI is wired up. Phase 4 (Projects) introduces the
///         first transactional command. <c>TransactionBehavior</c> (MediatR pipeline)
///         will be added in Phase 4.
///     </para>
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _db;
    private IDbContextTransaction? _transaction;

    public EfUnitOfWork(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already in progress for this unit of work.");
        }
        _transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException(
                "Cannot commit a unit of work — no transaction is in progress.");
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await _transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
