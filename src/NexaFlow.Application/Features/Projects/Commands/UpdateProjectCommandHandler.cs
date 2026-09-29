using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Projects.Commands;

public sealed class UpdateProjectCommandHandler : IRequestHandler<UpdateProjectCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ILogger<UpdateProjectCommandHandler> _logger;

    public UpdateProjectCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ILogger<UpdateProjectCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _logger = logger;
    }

    public async Task<Unit> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Resource-level authorization: load the project, verify tenant, verify the user is
        // at least a Contributor (Contributors can edit project details; only Owner can
        // manage members / delete / transfer ownership).
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var userId = _currentUser.UserId!.Value;

        if (request.NewName is not null)
            project.Rename(request.NewName, userId, now);

        if (request.NewDescription is not null)
            project.UpdateDescription(request.NewDescription, userId, now);

        if (request.NewStatus.HasValue)
            project.ChangeStatus(request.NewStatus.Value, userId, now);

        if (request.Dates is not null)
            project.SetDates(request.Dates.StartDateUtc, request.Dates.DueDateUtc, userId, now);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Project {ProjectId} updated by user {UserId}.",
            project.Id, userId);
        return Unit.Value;
    }
}
