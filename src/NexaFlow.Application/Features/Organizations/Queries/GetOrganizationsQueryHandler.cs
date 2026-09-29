using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Organizations.Dtos;

namespace NexaFlow.Application.Features.Organizations.Queries;

public sealed class GetOrganizationsQueryHandler : IRequestHandler<GetOrganizationsQuery, PagedResult<OrganizationSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetOrganizationsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<OrganizationSummaryDto>> Handle(
        GetOrganizationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            // The controller's [Authorize] should have caught this. Defense in depth:
            // return an empty result rather than throw.
            return PagedResult<OrganizationSummaryDto>.Empty(request.Page, request.PageSize);
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var (items, total) = await _db.GetPagedOrganizationsForUserAsync(userId, page, pageSize, cancellationToken);

        var dtos = items
            .Select(o => new OrganizationSummaryDto(o.Id, o.Name, o.Slug, o.OwnerUserId))
            .ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<OrganizationSummaryDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };
    }
}
