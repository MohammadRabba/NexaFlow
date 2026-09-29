using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Organizations.Commands;

public sealed class DeleteOrganizationCommandHandler : IRequestHandler<DeleteOrganizationCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DeleteOrganizationCommandHandler> _logger;

    public DeleteOrganizationCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<DeleteOrganizationCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Unit> Handle(DeleteOrganizationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        var org = await _db.FindOrganizationByIdAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        // Soft-delete: stamp the DeletedAtUtc; the AggregateRoot's IsDeleted property flips.
        // Memberships are not hard-deleted — they remain for audit. They are excluded from
        // future reads because GetPagedOrganizationsForUserAsync filters on DeletedAtUtc == null.
        var now = DateTimeOffset.UtcNow;
        org.SoftDelete(userId, now);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Organization {OrgId} soft-deleted by user {UserId} at {AtUtc}. " +
            "Memberships retained for audit; future reads exclude this org.",
            org.Id, userId, now);
        return Unit.Value;
    }
}
