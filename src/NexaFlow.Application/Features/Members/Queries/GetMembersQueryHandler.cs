using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Members.Dtos;

namespace NexaFlow.Application.Features.Members.Queries;

public sealed class GetMembersQueryHandler : IRequestHandler<GetMembersQuery, PagedResult<MemberDto>>
{
    private readonly IApplicationDbContext _db;

    public GetMembersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<MemberDto>> Handle(GetMembersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var (members, total) = await _db.GetPagedMembersAsync(
            request.OrganizationId, page, pageSize, cancellationToken);

        // For the email + display name fields, we'd need to join with users. Per ADR-002,
        // we'd typically add a "GetMembersWithUserAsync" named query method on IApplicationDbContext
        // that joins OrganizationMember → User at the SQL layer. For Phase 3 — to avoid
        // piling on more named queries — we accept the user id only and let the controller
        // hydrate the user view separately (a separate GET /api/users/{id} lookup, cached).
        // Pragmatic: most UIs show the user id and let the client resolve names.
        var dtos = members.Select(m => new MemberDto(
            Id: m.Id,
            UserId: m.UserId,
            Email: null,  // populated by a separate user query if needed
            DisplayName: null,
            Role: m.Role,
            IsActive: m.IsActive,
            JoinedAtUtc: m.CreatedAtUtc)).ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<MemberDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };
    }
}
