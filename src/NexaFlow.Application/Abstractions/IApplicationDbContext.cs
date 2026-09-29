using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction over the persistence layer. Application does NOT reference EF Core —
/// see ADR-002 for the trade-off and the "thin repository" extension path.
/// </summary>
public interface IApplicationDbContext
{
    // --- Read-side queryables (BCL System.Linq — sync LINQ composition is allowed) ---

    IQueryable<User> Users { get; }
    IQueryable<Organization> Organizations { get; }
    IQueryable<OrganizationMember> OrganizationMembers { get; }
    IQueryable<RefreshToken> RefreshTokens { get; }
    IQueryable<Project> Projects { get; }
    IQueryable<ProjectMember> ProjectMembers { get; }
    IQueryable<TaskItem> Tasks { get; }
    IQueryable<Label> Labels { get; }
    IQueryable<TaskLabel> TaskLabels { get; }
    IQueryable<Comment> Comments { get; }

    // --- Named async queries (thin repository pattern) ---

    // Users
    Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<User?> FindUserByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> EmailIsInUseAsync(string normalizedEmail, CancellationToken ct = default);

    // Refresh tokens
    Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<int> CountActiveRefreshTokensInFamilyAsync(Guid familyId, CancellationToken ct = default);

    // Organizations + memberships
    Task<bool> IsOrganizationSlugTakenAsync(string slug, CancellationToken ct = default);
    Task<Organization?> FindOrganizationByIdAsync(Guid id, CancellationToken ct = default);
    Task<Organization?> FindOrganizationWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<OrganizationMember?> FindMembershipAsync(Guid organizationId, Guid userId, CancellationToken ct = default);
    Task<(List<OrganizationMember> Items, long Total)> GetPagedMembersAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct = default);
    Task<(List<Organization> Items, long Total)> GetPagedOrganizationsForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Get all active projects in an organization (tracked, for cascade operations).</summary>
    Task<List<Project>> GetActiveProjectsForOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    // Projects + project members (Phase 4)
    /// <summary>
    ///     Load a project with all its active members. Bypasses the global tenant filter —
    /// the caller MUST verify the resolved tenant matches the project's OrganizationId via
    /// <c>EnsureMatchesTenantId</c> or equivalent BEFORE trusting the result.
    /// </summary>
    Task<Project?> FindProjectWithMembersAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    ///     Find a single project membership by (projectId, userId). Returns null if not found
    ///     or if the membership is inactive. Bypasses the global tenant filter.
    /// </summary>
    Task<ProjectMember?> FindProjectMembershipAsync(Guid projectId, Guid userId, CancellationToken ct = default);

    /// <summary>
    ///     Page the projects in an organization. Filters: status (optional), search by name (optional).
    /// Returns total + items.
    /// </summary>
    Task<(List<Project> Items, long Total)> GetPagedProjectsAsync(
        Guid organizationId,
        ProjectStatus? statusFilter,
        string? nameSearch,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    ///     Page the projects in an organization that the given user is a member of.
    /// Same filters/sorting as <see cref="GetPagedProjectsAsync" /> but the result is
    /// additionally constrained to projects where the user has a project_members row.
    /// </summary>
    Task<(List<Project> Items, long Total)> GetPagedProjectsForUserAsync(
        Guid organizationId,
        Guid userId,
        ProjectStatus? statusFilter,
        string? nameSearch,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Page the members of a project. Returns total + items.</summary>
    Task<(List<ProjectMember> Items, long Total)> GetPagedProjectMembersAsync(
        Guid projectId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    ///     Get all project memberships for a user in an organization. Used by the
    /// org-member-removal cascade to clean up orphaned project memberships.
    /// </summary>
    Task<List<ProjectMember>> GetProjectMembersForUserInOrgAsync(
        Guid organizationId, Guid userId, CancellationToken ct = default);

    // Tasks (Phase 5)

    /// <summary>
    ///     Load a task by id (tracked, for mutation). Bypasses the tenant filter —
    /// the caller must verify the task's ProjectId belongs to the resolved tenant
    /// via ProjectAccess BEFORE trusting the result.
    /// </summary>
    Task<TaskItem?> FindTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    ///     Page the tasks in a project. Filters: status, priority, assignee, due-date range.
    /// </summary>
    Task<(List<TaskItem> Items, long Total)> GetPagedTasksAsync(
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
        CancellationToken ct = default);

    // Labels (Phase 5)
    Task<List<Label>> GetLabelsForOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<Label?> FindLabelAsync(Guid labelId, Guid organizationId, CancellationToken ct = default);
    Task<TaskLabel?> FindTaskLabelAsync(Guid taskId, Guid labelId, Guid organizationId, CancellationToken ct = default);
    Task<List<Comment>> GetCommentsForTaskAsync(Guid taskId, CancellationToken ct = default);
    Task<Comment?> FindCommentAsync(Guid commentId, Guid organizationId, CancellationToken ct = default);

    // --- Mutations ---

    void Add<TEntity>(TEntity entity) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
