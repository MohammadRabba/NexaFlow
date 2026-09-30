using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Members.Commands;

public sealed class TransferOwnershipCommandHandler : IRequestHandler<TransferOwnershipCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly IAuditService _audit;
    private readonly ILogger<TransferOwnershipCommandHandler> _logger;

    public TransferOwnershipCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        IAuditService audit,
        ILogger<TransferOwnershipCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _audit = audit;
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

        // Audit OwnershipTransferred — same transaction. Records the previous + new owner.
        await _audit.RecordAsync(
            action: AuditAction.OwnershipTransferred,
            entity: "Organization",
            entityId: request.OrganizationId,
            oldValues: $"{{\"ownerUserId\":\"{actorId}\"}}",
            newValues: $"{{\"ownerUserId\":\"{request.ToUserId}\"}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Ownership of org {OrgId} transferred from user {ActorId} to user {TargetId}.",
            request.OrganizationId, actorId, request.ToUserId);
        return Unit.Value;
    }
}
