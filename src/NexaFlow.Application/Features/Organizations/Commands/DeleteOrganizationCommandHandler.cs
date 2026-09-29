using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Organizations.Commands;

public sealed class DeleteOrganizationCommandHandler : IRequestHandler<DeleteOrganizationCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ILogger<DeleteOrganizationCommandHandler> _logger;

    public DeleteOrganizationCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ILogger<DeleteOrganizationCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<Unit> Handle(DeleteOrganizationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");

        _currentTenant.EnsureMatchesTenantId(request.OrganizationId);

        // Load the org with its members so we can deactivate them (cascade).
        var org = await _db.FindOrganizationWithMembersAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        var now = DateTimeOffset.UtcNow;
        org.SoftDelete(userId, now);

        // Cascade: deactivate all org members so the tenant can no longer be resolved.
        // Without this, the TenantResolutionMiddleware would still accept X-Organization-Id
        // for this org because the membership rows still have IsActive=true.
        foreach (var member in org.Members)
        {
            member.Deactivate(userId, now);
        }

        // Cascade: soft-delete all projects in this org.
        var projects = await _db.GetActiveProjectsForOrganizationAsync(request.OrganizationId, cancellationToken);
        foreach (var project in projects)
        {
            project.SoftDelete(userId, now);
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Organization {OrgId} soft-deleted by user {UserId}. " +
            "All {MemberCount} memberships deactivated; {ProjectCount} projects soft-deleted.",
            org.Id, userId, org.Members.Count, projects.Count);
        return Unit.Value;
    }
}
