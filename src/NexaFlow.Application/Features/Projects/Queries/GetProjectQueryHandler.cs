using MediatR;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Dtos;

namespace NexaFlow.Application.Features.Projects.Queries;

public sealed class GetProjectQueryHandler : IRequestHandler<GetProjectQuery, ProjectDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ICacheService _cache;
    private readonly CacheOptions _cacheOptions;

    public GetProjectQueryHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ICacheService cache,
        IOptions<CacheOptions> cacheOptions)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
    }

    public async Task<ProjectDto?> Handle(GetProjectQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return null;
        if (!_currentTenant.IsTenantResolved)
            return null;

        var resolvedTenantId = _currentTenant.RequireTenantId();

        // Cache-aside: try Redis first. Key: project:{id}
        var cacheKey = $"project:{request.ProjectId}";
        var cached = await _cache.GetAsync<ProjectDto>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            // Verify access even on cache hit — authorization must not be bypassed.
            var project = await _db.FindProjectWithMembersAsync(request.ProjectId, cancellationToken);
            if (project is null || project.IsDeleted) return null;
            if (project.OrganizationId != resolvedTenantId) return null;
            if (!project.Members.Any(m => m.UserId == userId)) return null;
            return cached;
        }

        // Cache miss — query PostgreSQL
        var projectFromDb = await _db.FindProjectWithMembersAsync(request.ProjectId, cancellationToken);
        if (projectFromDb is null || projectFromDb.IsDeleted) return null;
        if (projectFromDb.OrganizationId != resolvedTenantId) return null;
        if (!projectFromDb.Members.Any(m => m.UserId == userId)) return null;

        var dto = new ProjectDto(
            projectFromDb.Id, projectFromDb.OrganizationId, projectFromDb.Name,
            projectFromDb.Description, projectFromDb.Status, projectFromDb.StartDateUtc,
            projectFromDb.DueDateUtc, projectFromDb.OwnerUserId,
            projectFromDb.CreatedAtUtc, projectFromDb.UpdatedAtUtc);

        await _cache.SetAsync(cacheKey, dto, _cacheOptions.ProjectCacheTtl, cancellationToken);

        return dto;
    }
}
