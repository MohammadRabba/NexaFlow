using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Members.Commands;

public sealed class TransferOwnershipCommandHandler : IRequestHandler<TransferOwnershipCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ILogger<TransferOwnershipCommandHandler> _logger;

    public TransferOwnershipCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ILogger<TransferOwnershipCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<Unit> Handle(TransferOwnershipCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } actorId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        // Cross-tenant guard.
        _currentTenant.EnsureMatchesTenantId(request.OrganizationId);

        // Load the org with members — TransferOwnership touches two members at once.
        var org = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        // Verify the actor is the current Owner. The [Authorize] policy guarantees the actor
        // holds a role that grants MemberTransferOwnership (only Owner does — see RolePermissions),
        // but we re-check here against the live aggregate (defense in depth + race protection:
        // if ownership was JUST transferred away, the actor is no longer Owner and must not
        // be able to transfer again).
        if (org.OwnerUserId != actorId)
        {
            throw new DomainException(
                "Only the current Owner can transfer ownership.",
                "NOT_OWNER");
        }

        // Apply the domain mutation. Organization.TransferOwnership will throw if the target
        // is not a member or is already the Owner.
        org.TransferOwnership(request.ToUserId, actorId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Ownership of org {OrgId} transferred from user {ActorId} to user {TargetId}.",
            request.OrganizationId, actorId, request.ToUserId);
        return Unit.Value;
    }
}
