using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Application.Features.Tasks.Dtos;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Tasks.Queries;

public sealed record GetTaskQuery(Guid ProjectId, Guid TaskId) : IRequest<TaskDto?>;

public sealed record GetTasksQuery(
    Guid ProjectId,
    TaskItemStatus? Status = null,
    TaskPriority? Priority = null,
    Guid? AssigneeId = null,
    DateTimeOffset? DueBefore = null,
    DateTimeOffset? DueAfter = null,
    string? SortBy = null,
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<TaskSummaryDto>>;

public sealed class GetTaskQueryHandler : IRequestHandler<GetTaskQuery, TaskDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _projectAccess;

    public GetTaskQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, ProjectAccess projectAccess)
    {
        _db = db;
        _currentUser = currentUser;
        _projectAccess = projectAccess;
    }

    public async Task<TaskDto?> Handle(GetTaskQuery request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);
        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken);
        if (task is null || task.ProjectId != request.ProjectId) return null;

        return new TaskDto(
            task.Id, task.ProjectId, task.Title, task.Description,
            task.Status, task.Priority, task.AssigneeId, task.ReporterId,
            task.DueDateUtc, task.CreatedAtUtc, task.UpdatedAtUtc);
    }
}

public sealed class GetTasksQueryHandler : IRequestHandler<GetTasksQuery, PagedResult<TaskSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ProjectAccess _projectAccess;

    public GetTasksQueryHandler(IApplicationDbContext db, ProjectAccess projectAccess)
    {
        _db = db;
        _projectAccess = projectAccess;
    }

    public async Task<PagedResult<TaskSummaryDto>> Handle(GetTasksQuery request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(1, request.Page);

        var (items, total) = await _db.GetPagedTasksAsync(
            request.ProjectId,
            request.Status,
            request.Priority,
            request.AssigneeId,
            request.DueBefore,
            request.DueAfter,
            request.SortBy,
            request.SortDescending,
            page,
            pageSize,
            cancellationToken);

        var dtos = items.Select(t => new TaskSummaryDto(
            t.Id, t.Title, t.Status, t.Priority, t.AssigneeId, t.DueDateUtc)).ToList();

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<TaskSummaryDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };
    }
}
