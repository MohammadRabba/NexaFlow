using NexaFlow.Application.Abstractions;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     In-memory fake of <see cref="IAuditService" /> for Application-layer unit tests.
///     Records every <c>RecordAsync</c> call so tests can assert on which audit entries
///     were produced. Optionally delegates to a real <c>IApplicationDbContext.Add</c>
///     when tests want to verify the row was actually queued for persistence — pass a
///     non-null <c>db</c> in the constructor to enable that.
/// </summary>
public sealed class FakeAuditService : IAuditService
{
    private readonly IApplicationDbContext? _db;

    /// <summary>Set to true to also call <c>_db.Add(entry)</c> on each RecordAsync, mirroring the real AuditService.</summary>
    private readonly bool _persistToDb;

    public FakeAuditService(IApplicationDbContext? db = null, bool persistToDb = false)
    {
        _db = db;
        _persistToDb = persistToDb;
    }

    public List<RecordedAuditEntry> Entries { get; } = [];

    public Task RecordAsync(
        string action,
        string entity,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        Guid? actorUserIdOverride = null,
        Guid? organizationIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new RecordedAuditEntry(
            Action: action,
            Entity: entity,
            EntityId: entityId,
            OldValues: oldValues,
            NewValues: newValues,
            ActorUserIdOverride: actorUserIdOverride,
            OrganizationIdOverride: organizationIdOverride);

        Entries.Add(entry);

        if (_persistToDb && _db is not null)
        {
            var auditLog = Domain.Entities.AuditLog.Create(
                userId: actorUserIdOverride,
                organizationId: organizationIdOverride,
                action: action,
                entity: entity,
                entityId: entityId,
                oldValues: oldValues,
                newValues: newValues,
                ipAddress: null,
                atUtc: DateTimeOffset.UtcNow);
            _db.Add(auditLog);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A single recorded audit-service call. Captured by <see cref="FakeAuditService" />
///     so tests can assert on the audit entries produced by a handler.
/// </summary>
public sealed record RecordedAuditEntry(
    string Action,
    string Entity,
    Guid? EntityId,
    string? OldValues,
    string? NewValues,
    Guid? ActorUserIdOverride,
    Guid? OrganizationIdOverride);
