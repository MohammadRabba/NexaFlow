using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Members.Commands;

public sealed class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<RemoveMemberCommandHandler> _logger;

    public RemoveMemberCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<RemoveMemberCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Unit> Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } actorId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        // Load the organization with members — RemoveMember is an aggregate operation
        // (cross-member invariant: "can't remove the Owner").
        var org = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        // Find the target membership.
        var target = org.Members.FirstOrDefault(m => m.UserId == request.TargetUserId && m.IsActive)
            ?? throw new NotFoundException("Membership", request.TargetUserId);

        // The domain's Organization.RemoveMember refuses to remove the Owner.
        // But we ALSO need to enforce the policy that only Owner or Admin can remove members,
        // and that an Admin cannot remove another Admin.
        var actor = org.Members.FirstOrDefault(m => m.UserId == actorId && m.IsActive)
            ?? throw new TenantViolationException(
                tenantId: request.OrganizationId,
                userId: actorId,
                message: "Actor is not a member of this organization.");

        // Self-removal is allowed for non-Owners (the user can leave voluntarily).
        // Owner cannot self-remove via this command — must transfer ownership first.
        var isSelfRemoval = request.TargetUserId == actorId;
        if (isSelfRemoval)
        {
            if (actor.Role == OrganizationRole.Owner)
            {
                throw new DomainException(
                    "Owner cannot leave the organization via this command. Transfer ownership first.",
                    "OWNER_CANNOT_LEAVE");
            }
            // Non-Owners can self-remove — no further authz check needed.
        }
        else
        {
            // Removing someone else requires Owner or Admin role.
            if (actor.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            {
                throw new DomainException(
                    "Only the Owner or an Admin can remove members.",
                    "INSUFFICIENT_ROLE");
            }
            // Admin cannot remove another Admin.
            if (actor.Role == OrganizationRole.Admin && target.Role == OrganizationRole.Admin)
            {
                throw new DomainException(
                    "An Admin cannot remove another Admin. Only the Owner can.",
                    "ADMIN_CANNOT_REMOVE_ADMIN");
            }
            // Admin cannot remove the Owner (the domain already refuses this, but defense in depth).
            if (target.Role == OrganizationRole.Owner)
            {
                throw new DomainException(
                    "Cannot remove the Owner. Transfer ownership first.",
                    "CANNOT_REMOVE_OWNER");
            }
        }

        // Apply the domain mutation. The aggregate also enforces "cannot remove the Owner".
        org.RemoveMember(request.TargetUserId, DateTimeOffset.UtcNow);

        // Hard-delete the membership row via the DbContext. Audit logs reference user_id +
        // organization_id, not the membership row — so we don't lose audit trail.
        _db.Remove(target);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {ActorId} removed user {TargetId} from org {OrgId}.",
            actorId, request.TargetUserId, request.OrganizationId);
        return Unit.Value;
    }
}
