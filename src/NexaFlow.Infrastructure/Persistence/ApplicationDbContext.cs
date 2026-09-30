using System.Linq.Expressions;
using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Common;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Infrastructure.Events;

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
    private readonly IEventSerializer? _eventSerializer;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUser,
        ITenantServiceAccessor tenantAccessor,
        IEventSerializer? eventSerializer = null)
        : base(options)
    {
        _currentUser = currentUser;
        _tenantAccessor = tenantAccessor;
        _eventSerializer = eventSerializer;
    }

    // EF Core's DbSet<T> implements IQueryable<T>, so the IApplicationDbContext contract
    // is satisfied by simply exposing the DbSets as IQueryable<T>. Mutation methods
    // delegate to DbContext.Set<T>() internally.

    IQueryable<User> IApplicationDbContext.Users => Users;
    IQueryable<Organization> IApplicationDbContext.Organizations => Organizations;
    IQueryable<OrganizationMember> IApplicationDbContext.OrganizationMembers => OrganizationMembers;
    IQueryable<RefreshToken> IApplicationDbContext.RefreshTokens => RefreshTokens;
    IQueryable<Project> IApplicationDbContext.Projects => Projects;
    IQueryable<ProjectMember> IApplicationDbContext.ProjectMembers => ProjectMembers;
    IQueryable<TaskItem> IApplicationDbContext.Tasks => Tasks;
    IQueryable<Label> IApplicationDbContext.Labels => Labels;
    IQueryable<TaskLabel> IApplicationDbContext.TaskLabels => TaskLabels;
    IQueryable<Comment> IApplicationDbContext.Comments => Comments;
    IQueryable<Notification> IApplicationDbContext.Notifications => Notifications;
    IQueryable<AuditLog> IApplicationDbContext.AuditLogs => AuditLogs;

    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

    async Task<List<Project>> IApplicationDbContext.GetActiveProjectsForOrganizationAsync(
        Guid organizationId, CancellationToken ct)
    {
        return await Projects
            .IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted)
            .ToListAsync(ct);
    }

    async Task<Project?> IApplicationDbContext.FindProjectWithMembersAsync(Guid projectId, CancellationToken ct)
    {
        // Bypass the tenant filter: the caller (handler) must verify the resolved tenant
        // matches the loaded project's OrganizationId via EnsureMatchesTenantId BEFORE
        // trusting the result. Loaded tracked (no AsNoTracking) — handlers mutate the
        // project + its Members collection.
        return await Projects
            .Include(p => p.Members)
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
    }

    Task<ProjectMember?> IApplicationDbContext.FindProjectMembershipAsync(
        Guid projectId, Guid userId, CancellationToken ct)
        => ProjectMembers
            .IgnoreQueryFilters()
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .FirstOrDefaultAsync(ct);

    async Task<(List<Project> Items, long Total)> IApplicationDbContext.GetPagedProjectsAsync(
        Guid organizationId,
        ProjectStatus? statusFilter,
        string? nameSearch,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        // The global query filter scopes by the ambient tenant. We additionally pass
        // organizationId explicitly so that if the ambient tenant is null, the query
        // returns no rows (the filter would have done this anyway).
        var query = Projects.Where(p =>
            p.OrganizationId == organizationId && !p.IsDeleted);

        if (statusFilter.HasValue)
        {
            query = query.Where(p => p.Status == statusFilter.Value);
        }
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            // Case-insensitive substring search on Name. Uses EF.Functions.ILike for
            // PostgreSQL native case-insensitive ILIKE; the index on (organization_id, status)
            // is NOT used by this query path, but for a tenant-scoped list this is fine —
            // the cardinality per tenant is bounded.
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{nameSearch}%"));
        }

        // Sorting. Default to CreatedAtUtc descending (most-recent first).
        query = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("name", true) => query.OrderByDescending(p => p.Name),
            ("name", false) => query.OrderBy(p => p.Name),
            ("status", true) => query.OrderByDescending(p => p.Status),
            ("status", false) => query.OrderBy(p => p.Status),
            ("duedate", true) => query.OrderByDescending(p => p.DueDateUtc),
            ("duedate", false) => query.OrderBy(p => p.DueDateUtc),
            ("createdat", true) => query.OrderByDescending(p => p.CreatedAtUtc),
            ("createdat", false) => query.OrderBy(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)  // default
        };

        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    async Task<(List<Project> Items, long Total)> IApplicationDbContext.GetPagedProjectsForUserAsync(
        Guid organizationId,
        Guid userId,
        ProjectStatus? statusFilter,
        string? nameSearch,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        // JOIN projects → project_members by (project_id) → filter on user_id.
        // The global tenant filter applies to both Projects and ProjectMembers, so the
        // ambient tenant is enforced. We additionally pass organizationId explicitly.
        var baseQuery = from p in Projects
                        join m in ProjectMembers on p.Id equals m.ProjectId
                        where p.OrganizationId == organizationId
                           && !p.IsDeleted
                           && m.UserId == userId
                        select p;

        // produce duplicates if the user has multiple rows for the same project (shouldn't
        // happen due to the unique constraint, but defensive).
        var query = baseQuery;

        if (statusFilter.HasValue)
        {
            query = query.Where(p => p.Status == statusFilter.Value);
        }
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{nameSearch}%"));
        }

        query = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("name", true) => query.OrderByDescending(p => p.Name),
            ("name", false) => query.OrderBy(p => p.Name),
            ("status", true) => query.OrderByDescending(p => p.Status),
            ("status", false) => query.OrderBy(p => p.Status),
            ("duedate", true) => query.OrderByDescending(p => p.DueDateUtc),
            ("duedate", false) => query.OrderBy(p => p.DueDateUtc),
            ("createdat", true) => query.OrderByDescending(p => p.CreatedAtUtc),
            ("createdat", false) => query.OrderBy(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };

        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    async Task<(List<ProjectMember> Items, long Total)> IApplicationDbContext.GetPagedProjectMembersAsync(
        Guid projectId, int page, int pageSize, CancellationToken ct)
    {
        var query = ProjectMembers
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.CreatedAtUtc);
        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    async Task<List<ProjectMember>> IApplicationDbContext.GetProjectMembersForUserInOrgAsync(
        Guid organizationId, Guid userId, CancellationToken ct)
    {
        return await ProjectMembers
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .ToListAsync(ct);
    }

    Task<TaskItem?> IApplicationDbContext.FindTaskAsync(Guid taskId, CancellationToken ct)
        => Tasks
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

    async Task<(List<TaskItem> Items, long Total)> IApplicationDbContext.GetPagedTasksAsync(
        Guid projectId,
        TaskItemStatus? statusFilter,
        TaskPriority? priorityFilter,
        Guid? assigneeFilter,
        DateTimeOffset? dueBefore,
        DateTimeOffset? dueAfter,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = Tasks.Where(t => t.ProjectId == projectId && !t.IsDeleted);

        if (statusFilter.HasValue)
            query = query.Where(t => t.Status == statusFilter.Value);
        if (priorityFilter.HasValue)
            query = query.Where(t => t.Priority == priorityFilter.Value);
        if (assigneeFilter.HasValue)
            query = query.Where(t => t.AssigneeId == assigneeFilter.Value);
        if (dueBefore.HasValue)
            query = query.Where(t => t.DueDateUtc <= dueBefore.Value);
        if (dueAfter.HasValue)
            query = query.Where(t => t.DueDateUtc >= dueAfter.Value);

        query = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("title", true) => query.OrderByDescending(t => t.Title),
            ("title", false) => query.OrderBy(t => t.Title),
            ("priority", true) => query.OrderByDescending(t => t.Priority),
            ("priority", false) => query.OrderBy(t => t.Priority),
            ("duedate", true) => query.OrderByDescending(t => t.DueDateUtc),
            ("duedate", false) => query.OrderBy(t => t.DueDateUtc),
            ("createdat", true) => query.OrderByDescending(t => t.CreatedAtUtc),
            ("createdat", false) => query.OrderBy(t => t.CreatedAtUtc),
            _ => query.OrderByDescending(t => t.CreatedAtUtc)
        };

        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    async Task<List<Label>> IApplicationDbContext.GetLabelsForOrganizationAsync(
        Guid organizationId, CancellationToken ct)
    {
        return await Labels
            .Where(l => l.OrganizationId == organizationId)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    async Task<List<Comment>> IApplicationDbContext.GetCommentsForTaskAsync(
        Guid taskId, CancellationToken ct)
    {
        return await Comments
            .Where(c => c.TaskId == taskId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);
    }

    Task<Label?> IApplicationDbContext.FindLabelAsync(Guid labelId, Guid organizationId, CancellationToken ct)
        => Labels.FirstOrDefaultAsync(l => l.Id == labelId && l.OrganizationId == organizationId, ct);

    Task<TaskLabel?> IApplicationDbContext.FindTaskLabelAsync(
        Guid taskId, Guid labelId, Guid organizationId, CancellationToken ct)
        => TaskLabels.FirstOrDefaultAsync(
            tl => tl.TaskId == taskId && tl.LabelId == labelId && tl.OrganizationId == organizationId, ct);

    Task<Comment?> IApplicationDbContext.FindCommentAsync(Guid commentId, Guid organizationId, CancellationToken ct)
        => Comments.FirstOrDefaultAsync(
            c => c.Id == commentId && c.OrganizationId == organizationId && !c.IsDeleted, ct);

    async Task<List<Notification>> IApplicationDbContext.GetNotificationsForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct)
    {
        var query = Notifications.Where(n => n.RecipientUserId == userId && !n.IsDeleted);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        return await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    Task<Notification?> IApplicationDbContext.FindNotificationAsync(
        Guid notificationId, Guid userId, CancellationToken ct)
        => Notifications.FirstOrDefaultAsync(
            n => n.Id == notificationId && n.RecipientUserId == userId && !n.IsDeleted, ct);

    // --- Audit logs (Phase 8) ---
    // AuditLog is NOT ITenantEntity — no global query filter applies. We filter
    // explicitly by organization_id (the parameter), so cross-tenant rows are
    // excluded by the WHERE clause, not by an ambient filter.

    async Task<(List<AuditLog> Items, long Total)> IApplicationDbContext.GetPagedAuditLogsAsync(
        Guid organizationId,
        string? actionFilter,
        Guid? userIdFilter,
        string? entityFilter,
        Guid? entityIdFilter,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = AuditLogs.Where(a => a.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(actionFilter))
            query = query.Where(a => a.Action == actionFilter);
        if (userIdFilter.HasValue)
            query = query.Where(a => a.UserId == userIdFilter.Value);
        if (!string.IsNullOrWhiteSpace(entityFilter))
            query = query.Where(a => a.Entity == entityFilter);
        if (entityIdFilter.HasValue)
            query = query.Where(a => a.EntityId == entityIdFilter.Value);
        if (fromUtc.HasValue)
            query = query.Where(a => a.Timestamp >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(a => a.Timestamp <= toUtc.Value);

        query = query.OrderByDescending(a => a.Timestamp);

        var total = await query.LongCountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    async Task<(List<AuditLog> Items, long Total)> IApplicationDbContext.GetPagedAuditLogsForUserAsync(
        Guid userId,
        string? actionFilter,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = AuditLogs.Where(a => a.UserId == userId);

        if (!string.IsNullOrWhiteSpace(actionFilter))
            query = query.Where(a => a.Action == actionFilter);
        if (fromUtc.HasValue)
            query = query.Where(a => a.Timestamp >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(a => a.Timestamp <= toUtc.Value);

        query = query.OrderByDescending(a => a.Timestamp);

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

        // Phase 6: Transactional Outbox — intercept domain events from tracked aggregates
        // and create outbox messages in the SAME transaction. This guarantees:
        // business state change + outbox record succeed atomically, or neither does.
        if (_eventSerializer is not null)
        {
            var domainEvents = new List<(Entity Entity, IDomainEvent Event)>();
            foreach (var entry in ChangeTracker.Entries<Entity>())
            {
                if (entry.Entity.DomainEvents.Count > 0)
                {
                    foreach (var evt in entry.Entity.DomainEvents)
                    {
                        domainEvents.Add((entry.Entity, evt));
                    }
                }
            }

            foreach (var (entity, evt) in domainEvents)
            {
                var (eventType, payload) = _eventSerializer.Serialize(evt);
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = eventType,
                    Payload = payload,
                    OccurredOnUtc = evt.OccurredOnUtc,
                    AttemptCount = 0
                });
                entity.ClearDomainEvents();
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
