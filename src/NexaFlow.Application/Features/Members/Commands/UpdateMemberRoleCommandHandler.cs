using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Members.Commands;

public sealed class UpdateMemberRoleCommandHandler : IRequestHandler<UpdateMemberRoleCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ILogger<UpdateMemberRoleCommandHandler> _logger;

    public UpdateMemberRoleCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ILogger<UpdateMemberRoleCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<Unit> Handle(UpdateMemberRoleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } actorId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        // Load the membership bypassing the tenant filter (the controller's [Authorize] already
        // verified the actor is a member of the resolved tenant). We bypass the filter so that
        // if the caller targets a membership in another org, we still see the row and can
        // reject the operation explicitly (rather than silently failing with "not found").
        var target = await _db.FindMembershipAsync(
            request.OrganizationId, request.TargetUserId, cancellationToken)
            ?? throw new NotFoundException("Membership", (request.OrganizationId, request.TargetUserId));

        // Load the actor's membership to read their role (database authoritative).
        var actor = await _db.FindMembershipAsync(
            request.OrganizationId, actorId, cancellationToken)
            ?? throw new TenantViolationException(
                tenantId: request.OrganizationId,
                userId: actorId,
                message: "Actor is not a member of this organization.");

        // --- Authorization rules (not in domain — these are policy decisions) ---
        // Per directive point 3: "Admin cannot modify another Admin"
        //                    and  "Owner can modify anyone"
        if (actor.Role != OrganizationRole.Owner)
        {
            // Admin/Member cannot change an Admin's role
            if (target.Role == OrganizationRole.Admin)
            {
                throw new DomainException(
                    "Only the Owner can modify an Admin's role.",
                    "CANNOT_MODIFY_ADMIN");
            }
            // Admin can change Member/Viewer; Member cannot change anyone's role
            if (actor.Role != OrganizationRole.Admin)
            {
                throw new DomainException(
                    "Only the Owner or an Admin can change a member's role.",
                    "INSUFFICIENT_ROLE");
            }
        }

        // Apply the domain mutation. The domain method already rejects Owner role changes
        // (defense in depth — the domain doesn't trust that authorization caught it).
        target.ChangeRole(request.NewRole, actorId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {ActorId} changed role of user {TargetId} to {NewRole} in org {OrgId}.",
            actorId, request.TargetUserId, request.NewRole, request.OrganizationId);
        return Unit.Value;
    }
}
