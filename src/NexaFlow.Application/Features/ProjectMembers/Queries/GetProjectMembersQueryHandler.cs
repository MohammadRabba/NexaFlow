using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.ProjectMembers.Dtos;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.ProjectMembers.Queries;

public sealed class GetProjectMembersQueryHandler : IRequestHandler<GetProjectMembersQuery, PagedResult<ProjectMemberDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ProjectAccess _access;

    public GetProjectMembersQueryHandler(IApplicationDbContext db, ProjectAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<PagedResult<ProjectMemberDto>> Handle(
        GetProjectMembersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The actor must be at least a Reader on the project (any project member can list members).
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var (items, total) = await _db.GetPagedProjectMembersAsync(
            project.Id, page, pageSize, cancellationToken);

        var dtos = items.Select(m => new ProjectMemberDto(
            Id: m.Id,
            ProjectId: m.ProjectId,
            UserId: m.UserId,
            Role: m.Role,
            JoinedAtUtc: m.CreatedAtUtc)).ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<ProjectMemberDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };
    }
}
