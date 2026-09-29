using MediatR;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Dtos;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Projects.Queries;

public sealed class GetProjectQueryHandler : IRequestHandler<GetProjectQuery, ProjectDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public GetProjectQueryHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public async Task<ProjectDto?> Handle(GetProjectQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return null;
        if (!_currentTenant.IsTenantResolved)
            return null;

        var resolvedTenantId = _currentTenant.RequireTenantId();
        var project = await _db.FindProjectWithMembersAsync(request.ProjectId, cancellationToken);
        if (project is null || project.IsDeleted) return null;
        if (project.OrganizationId != resolvedTenantId) return null;

        // Resource-level: the user must be a member of this project.
        if (!project.Members.Any(m => m.UserId == userId))
            return null;

        return new ProjectDto(
            Id: project.Id,
            OrganizationId: project.OrganizationId,
            Name: project.Name,
            Description: project.Description,
            Status: project.Status,
            StartDateUtc: project.StartDateUtc,
            DueDateUtc: project.DueDateUtc,
            OwnerUserId: project.OwnerUserId,
            CreatedAtUtc: project.CreatedAtUtc,
            UpdatedAtUtc: project.UpdatedAtUtc);
    }
}
