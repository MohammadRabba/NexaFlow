using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.ProjectMembers.Dtos;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

public sealed class AddProjectMemberCommandHandler : IRequestHandler<AddProjectMemberCommand, ProjectMemberDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ILogger<AddProjectMemberCommandHandler> _logger;

    public AddProjectMemberCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ILogger<AddProjectMemberCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _logger = logger;
    }

    public async Task<ProjectMemberDto> Handle(AddProjectMemberCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Only the project Owner can add members.
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Owner, cancellationToken);

        // The invitee must be a registered user.
        var invitee = await _db.FindUserByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException(
                "User not found. The user must be registered before being added to a project.",
                "USER_NOT_FOUND");

        // Prevent adding yourself (the Owner is already a member).
        if (invitee.Id == _currentUser.UserId)
            throw new DomainException("You are already a member of this project.", "USER_ALREADY_MEMBER");

        // The domain aggregate rejects duplicates; we also check here to give a friendlier error.
        if (project.Members.Any(m => m.UserId == invitee.Id))
            throw new DomainException(
                $"User is already a member of this project.",
                "USER_ALREADY_MEMBER");

        var member = project.AddMember(invitee.Id, request.Role, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {ActorId} added user {AddedId} to project {ProjectId} as {Role}.",
            _currentUser.UserId, invitee.Id, project.Id, request.Role);

        return new ProjectMemberDto(
            Id: member.Id,
            ProjectId: project.Id,
            UserId: member.UserId,
            Role: member.Role,
            JoinedAtUtc: member.CreatedAtUtc);
    }
}
