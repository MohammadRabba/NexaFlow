using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Comments.Commands;

public sealed record GetCommentsQuery(Guid ProjectId, Guid TaskId) : IRequest<List<Comment>>;

public sealed class GetCommentsQueryHandler : IRequestHandler<GetCommentsQuery, List<Comment>>
{
    private readonly IApplicationDbContext _db;
    private readonly ProjectAccess _projectAccess;

    public GetCommentsQueryHandler(IApplicationDbContext db, ProjectAccess projectAccess)
    {
        _db = db;
        _projectAccess = projectAccess;
    }

    public async Task<List<Comment>> Handle(GetCommentsQuery request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        return await _db.GetCommentsForTaskAsync(request.TaskId, cancellationToken);
    }
}
