using MediatR;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Organizations.Dtos;

namespace NexaFlow.Application.Features.Organizations.Queries;

public sealed class GetOrganizationQueryHandler : IRequestHandler<GetOrganizationQuery, OrganizationDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICacheService _cache;
    private readonly CacheOptions _cacheOptions;

    public GetOrganizationQueryHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICacheService cache,
        IOptions<CacheOptions> cacheOptions)
    {
        _db = db;
        _currentUser = currentUser;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
    }

    public async Task<OrganizationDto?> Handle(GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return null;

        // Cache-aside: try Redis first. Key: organization:{id}
        var cacheKey = $"organization:{request.OrganizationId}";
        var cached = await _cache.GetAsync<OrganizationDto>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            // Still verify membership — cached org data is fine, but authorization
            // must not be bypassed by stale cache (section 23).
            var org = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken);
            if (org is null || org.IsDeleted) return null;
            if (!org.Members.Any(m => m.UserId == userId && m.IsActive)) return null;
            return cached;
        }

        // Cache miss — query PostgreSQL
        var orgFromDb = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken);
        if (orgFromDb is null || orgFromDb.IsDeleted) return null;

        if (!orgFromDb.Members.Any(m => m.UserId == userId && m.IsActive)) return null;

        var dto = new OrganizationDto(
            orgFromDb.Id, orgFromDb.Name, orgFromDb.Slug, orgFromDb.OwnerUserId, orgFromDb.CreatedAtUtc);

        // Populate cache
        await _cache.SetAsync(cacheKey, dto, _cacheOptions.OrganizationCacheTtl, cancellationToken);

        return dto;
    }
}
