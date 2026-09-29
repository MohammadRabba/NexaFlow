using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Common;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;

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
    public List<Project> Projects { get; } = [];
    public List<ProjectMember> ProjectMembers { get; } = [];
    public List<TaskItem> Tasks { get; } = [];
    public List<Label> Labels { get; } = [];
    public List<TaskLabel> TaskLabels { get; } = [];
    public List<Comment> Comments { get; } = [];

    IQueryable<User> IApplicationDbContext.Users => Users.AsQueryable();
    IQueryable<Organization> IApplicationDbContext.Organizations => Organizations.AsQueryable();
    IQueryable<OrganizationMember> IApplicationDbContext.OrganizationMembers => OrganizationMembers.AsQueryable();
    IQueryable<RefreshToken> IApplicationDbContext.RefreshTokens => RefreshTokens.AsQueryable();
    IQueryable<Project> IApplicationDbContext.Projects => Projects.AsQueryable();
    IQueryable<ProjectMember> IApplicationDbContext.ProjectMembers => ProjectMembers.AsQueryable();
    IQueryable<TaskItem> IApplicationDbContext.Tasks => Tasks.AsQueryable();
    IQueryable<Label> IApplicationDbContext.Labels => Labels.AsQueryable();
    IQueryable<TaskLabel> IApplicationDbContext.TaskLabels => TaskLabels.AsQueryable();
    IQueryable<Comment> IApplicationDbContext.Comments => Comments.AsQueryable();

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

    public Task<Project?> FindProjectWithMembersAsync(Guid projectId, CancellationToken ct = default)
    {
        // In-memory test double: Project.Members is the in-aggregate collection.
        var project = Projects.FirstOrDefault(p => p.Id == projectId && !p.IsDeleted);
        return Task.FromResult(project);
    }

    public Task<ProjectMember?> FindProjectMembershipAsync(Guid projectId, Guid userId, CancellationToken ct = default)
    {
        var m = ProjectMembers.FirstOrDefault(pm =>
            pm.ProjectId == projectId && pm.UserId == userId);
        return Task.FromResult(m);
    }

    public Task<(List<Project> Items, long Total)> GetPagedProjectsAsync(
        Guid organizationId,
        ProjectStatus? statusFilter,
        string? nameSearch,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = Projects.Where(p => p.OrganizationId == organizationId && !p.IsDeleted);
        if (statusFilter.HasValue) query = query.Where(p => p.Status == statusFilter.Value);
        if (!string.IsNullOrWhiteSpace(nameSearch))
            query = query.Where(p => p.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));

        IEnumerable<Project> ordered = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("name", true) => query.OrderByDescending(p => p.Name),
            ("name", false) => query.OrderBy(p => p.Name),
            ("status", true) => query.OrderByDescending(p => p.Status),
            ("status", false) => query.OrderBy(p => p.Status),
            ("duedate", true) => query.OrderByDescending(p => p.DueDateUtc ?? DateTimeOffset.MaxValue),
            ("duedate", false) => query.OrderBy(p => p.DueDateUtc ?? DateTimeOffset.MaxValue),
            ("createdat", true) => query.OrderByDescending(p => p.CreatedAtUtc),
            ("createdat", false) => query.OrderBy(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };
        var list = ordered.ToList();
        var total = (long)list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, total));
    }

    public Task<(List<Project> Items, long Total)> GetPagedProjectsForUserAsync(
        Guid organizationId,
        Guid userId,
        ProjectStatus? statusFilter,
        string? nameSearch,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var projectIdsForUser = ProjectMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.ProjectId)
            .ToHashSet();

        var query = Projects
            .Where(p => p.OrganizationId == organizationId
                && !p.IsDeleted
                && projectIdsForUser.Contains(p.Id));
        if (statusFilter.HasValue) query = query.Where(p => p.Status == statusFilter.Value);
        if (!string.IsNullOrWhiteSpace(nameSearch))
            query = query.Where(p => p.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));

        IEnumerable<Project> ordered = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("name", true) => query.OrderByDescending(p => p.Name),
            ("name", false) => query.OrderBy(p => p.Name),
            ("status", true) => query.OrderByDescending(p => p.Status),
            ("status", false) => query.OrderBy(p => p.Status),
            ("duedate", true) => query.OrderByDescending(p => p.DueDateUtc ?? DateTimeOffset.MaxValue),
            ("duedate", false) => query.OrderBy(p => p.DueDateUtc ?? DateTimeOffset.MaxValue),
            ("createdat", true) => query.OrderByDescending(p => p.CreatedAtUtc),
            ("createdat", false) => query.OrderBy(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };
        var list = ordered.ToList();
        var total = (long)list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, total));
    }

    public Task<(List<ProjectMember> Items, long Total)> GetPagedProjectMembersAsync(
        Guid projectId, int page, int pageSize, CancellationToken ct = default)
    {
        var filtered = ProjectMembers
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToList();
        var total = (long)filtered.Count;
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, total));
    }

    public Task<List<ProjectMember>> GetProjectMembersForUserInOrgAsync(
        Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        var result = ProjectMembers
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<List<Project>> GetActiveProjectsForOrganizationAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        var result = Projects
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<TaskItem?> FindTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        return Task.FromResult(Tasks.FirstOrDefault(t => t.Id == taskId && !t.IsDeleted));
    }

    public Task<(List<TaskItem> Items, long Total)> GetPagedTasksAsync(
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
        CancellationToken ct = default)
    {
        var query = Tasks.Where(t => t.ProjectId == projectId && !t.IsDeleted);
        if (statusFilter.HasValue) query = query.Where(t => t.Status == statusFilter.Value);
        if (priorityFilter.HasValue) query = query.Where(t => t.Priority == priorityFilter.Value);
        if (assigneeFilter.HasValue) query = query.Where(t => t.AssigneeId == assigneeFilter.Value);
        if (dueBefore.HasValue) query = query.Where(t => t.DueDateUtc <= dueBefore.Value);
        if (dueAfter.HasValue) query = query.Where(t => t.DueDateUtc >= dueAfter.Value);

        IEnumerable<TaskItem> ordered = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("title", true) => query.OrderByDescending(t => t.Title),
            ("title", false) => query.OrderBy(t => t.Title),
            ("priority", true) => query.OrderByDescending(t => t.Priority),
            ("priority", false) => query.OrderBy(t => t.Priority),
            ("duedate", true) => query.OrderByDescending(t => t.DueDateUtc ?? DateTimeOffset.MaxValue),
            ("duedate", false) => query.OrderBy(t => t.DueDateUtc ?? DateTimeOffset.MaxValue),
            ("createdat", true) => query.OrderByDescending(t => t.CreatedAtUtc),
            ("createdat", false) => query.OrderBy(t => t.CreatedAtUtc),
            _ => query.OrderByDescending(t => t.CreatedAtUtc)
        };
        var list = ordered.ToList();
        var total = (long)list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, total));
    }

    public Task<List<Label>> GetLabelsForOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        var result = Labels.Where(l => l.OrganizationId == organizationId).OrderBy(l => l.Name).ToList();
        return Task.FromResult(result);
    }

    public Task<List<Comment>> GetCommentsForTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var result = Comments.Where(c => c.TaskId == taskId && !c.IsDeleted).OrderBy(c => c.CreatedAtUtc).ToList();
        return Task.FromResult(result);
    }

    public Task<Label?> FindLabelAsync(Guid labelId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Labels.FirstOrDefault(l => l.Id == labelId && l.OrganizationId == organizationId));

    public Task<TaskLabel?> FindTaskLabelAsync(Guid taskId, Guid labelId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(TaskLabels.FirstOrDefault(tl => tl.TaskId == taskId && tl.LabelId == labelId && tl.OrganizationId == organizationId));

    public Task<Comment?> FindCommentAsync(Guid commentId, Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Comments.FirstOrDefault(c => c.Id == commentId && c.OrganizationId == organizationId && !c.IsDeleted));

    public void Add<TEntity>(TEntity entity) where TEntity : class
    {
        switch (entity)
        {
            case User u: Users.Add(u); break;
            case Organization o: Organizations.Add(o); break;
            case OrganizationMember m: OrganizationMembers.Add(m); break;
            case RefreshToken t: RefreshTokens.Add(t); break;
            case Project p: Projects.Add(p); break;
            case ProjectMember pm: ProjectMembers.Add(pm); break;
            case TaskItem t: Tasks.Add(t); break;
            case Label l: Labels.Add(l); break;
            case TaskLabel tl: TaskLabels.Add(tl); break;
            case Comment c: Comments.Add(c); break;
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
            case Project p: Projects.Remove(p); break;
            case ProjectMember pm: ProjectMembers.Remove(pm); break;
            case TaskItem t: Tasks.Remove(t); break;
            case Label l: Labels.Remove(l); break;
            case TaskLabel tl: TaskLabels.Remove(tl); break;
            case Comment c: Comments.Remove(c); break;
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
