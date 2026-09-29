using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

public sealed class UpdateProjectMemberRoleCommandHandler : IRequestHandler<UpdateProjectMemberRoleCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ILogger<UpdateProjectMemberRoleCommandHandler> _logger;

    public UpdateProjectMemberRoleCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ILogger<UpdateProjectMemberRoleCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _logger = logger;
    }

    public async Task<Unit> Handle(UpdateProjectMemberRoleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Only the project Owner can change roles.
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Owner, cancellationToken);

        var target = project.Members.FirstOrDefault(m => m.UserId == request.TargetUserId)
            ?? throw new NotFoundException("ProjectMember", request.TargetUserId);

        // The domain's ChangeRole rejects changing an Owner's role.
        target.ChangeRole(request.NewRole, _currentUser.UserId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {ActorId} changed role of user {TargetId} to {NewRole} in project {ProjectId}.",
            _currentUser.UserId, request.TargetUserId, request.NewRole, project.Id);
        return Unit.Value;
    }
}
