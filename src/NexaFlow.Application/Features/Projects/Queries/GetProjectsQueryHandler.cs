using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Projects.Dtos;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Projects.Queries;

public sealed class GetProjectsQueryHandler : IRequestHandler<GetProjectsQuery, PagedResult<ProjectSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public GetProjectsQueryHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public async Task<PagedResult<ProjectSummaryDto>> Handle(
        GetProjectsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return PagedResult<ProjectSummaryDto>.Empty(request.Page, request.PageSize);
        if (!_currentTenant.IsTenantResolved)
            return PagedResult<ProjectSummaryDto>.Empty(request.Page, request.PageSize);

        var organizationId = _currentTenant.RequireTenantId();
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var (items, total) = await _db.GetPagedProjectsForUserAsync(
            organizationId,
            userId,
            request.Status,
            request.Search,
            request.SortBy,
            request.SortDescending,
            page,
            pageSize,
            cancellationToken);

        var dtos = items.Select(p => new ProjectSummaryDto(
            p.Id, p.Name, p.Status, p.OwnerUserId, p.DueDateUtc)).ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<ProjectSummaryDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };
    }
}
