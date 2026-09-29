using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

public sealed class RemoveProjectMemberCommandHandler : IRequestHandler<RemoveProjectMemberCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ILogger<RemoveProjectMemberCommandHandler> _logger;

    public RemoveProjectMemberCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ILogger<RemoveProjectMemberCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _logger = logger;
    }

    public async Task<Unit> Handle(RemoveProjectMemberCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The actor must be at least a Contributor to remove members. The domain's
        // Project.RemoveMember refuses the Owner; we also enforce that only Owners
        // can remove others (Contributors can self-remove).
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);

        var actorId = _currentUser.UserId!.Value;
        var isSelfRemoval = request.TargetUserId == actorId;

        var target = project.Members.FirstOrDefault(m => m.UserId == request.TargetUserId)
            ?? throw new NotFoundException("ProjectMember", request.TargetUserId);

        if (target.Role == ProjectMemberRole.Owner)
            throw new DomainException(
                "Cannot remove the project Owner. Transfer ownership first.",
                "CANNOT_REMOVE_OWNER");

        if (isSelfRemoval)
        {
            // Self-removal is allowed for non-Owners (just verified above).
        }
        else
        {
            // Removing someone else requires Owner role.
            var actor = project.Members.FirstOrDefault(m => m.UserId == actorId)
                ?? throw new NotFoundException("Project", project.Id);  // shouldn't happen — _access verified it
            if (actor.Role != ProjectMemberRole.Owner)
                throw new DomainException(
                    "Only the project Owner can remove other members.",
                    "INSUFFICIENT_PROJECT_ROLE");
        }

        project.RemoveMember(request.TargetUserId, DateTimeOffset.UtcNow);
        // Also remove from the DbContext's ProjectMembers set (the domain's RemoveMember
        // removes from the in-aggregate collection, but the row in project_members is
        // still tracked).
        _db.Remove(target);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {ActorId} removed user {TargetId} from project {ProjectId}.",
            actorId, request.TargetUserId, project.Id);
        return Unit.Value;
    }
}
