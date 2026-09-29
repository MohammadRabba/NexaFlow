using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

public sealed class TransferProjectOwnershipCommandHandler : IRequestHandler<TransferProjectOwnershipCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ILogger<TransferProjectOwnershipCommandHandler> _logger;

    public TransferProjectOwnershipCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ILogger<TransferProjectOwnershipCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _logger = logger;
    }

    public async Task<Unit> Handle(TransferProjectOwnershipCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Only the current project Owner can transfer ownership.
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Owner, cancellationToken);

        // Live re-check: the actor must be the current Owner. The ProjectAccess check
        // verified the user is at least an Owner (role check), but a race could exist
        // where ownership was JUST transferred. Defensive.
        if (project.OwnerUserId != _currentUser.UserId)
            throw new DomainException("Only the current Owner can transfer ownership.", "NOT_PROJECT_OWNER");

        project.TransferOwnership(request.ToUserId, _currentUser.UserId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Ownership of project {ProjectId} transferred from user {ActorId} to user {TargetId}.",
            project.Id, _currentUser.UserId, request.ToUserId);
        return Unit.Value;
    }
}
