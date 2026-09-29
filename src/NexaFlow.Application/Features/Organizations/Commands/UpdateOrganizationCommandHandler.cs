using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Organizations.Commands;

public sealed class UpdateOrganizationCommandHandler : IRequestHandler<UpdateOrganizationCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ILogger<UpdateOrganizationCommandHandler> _logger;

    public UpdateOrganizationCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ILogger<UpdateOrganizationCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<Unit> Handle(UpdateOrganizationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        // Cross-tenant guard: URL's organizationId must match the resolved tenant.
        _currentTenant.EnsureMatchesTenantId(request.OrganizationId);

        var org = await _db.FindOrganizationByIdAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Organization), request.OrganizationId);

        // Note: the [Authorize] policy guarantees OrganizationUpdate permission. We don't
        // re-check the role here. The handler is focused on the domain mutation.
        org.Rename(request.NewName, userId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrgId} renamed by user {UserId}.", org.Id, userId);
        return Unit.Value;
    }
}
