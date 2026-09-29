using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Projects.Commands;

public sealed class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, ProjectDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ILogger<CreateProjectCommandHandler> _logger;

    public CreateProjectCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ILogger<CreateProjectCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            throw new DomainException("Authenticated user required.", "UNAUTHENTICATED");
        }

        // Tenant must be resolved (the [Authorize(Policy = ProjectCreate)] already verified
        // the user holds a role granting project.create in the resolved tenant).
        // RequireTenantId throws if no tenant is resolved — fail-closed.
        var organizationId = _currentTenant.RequireTenantId();

        var now = DateTimeOffset.UtcNow;
        var project = Project.Create(
            organizationId: organizationId,
            name: request.Name,
            description: request.Description ?? string.Empty,
            ownerUserId: userId,
            atUtc: now);

        // Apply the requested dates — the domain validates DueDate >= StartDate.
        if (request.StartDateUtc.HasValue || request.DueDateUtc.HasValue)
        {
            project.SetDates(request.StartDateUtc, request.DueDateUtc, userId, now);
        }

        _db.Add(project);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Project {ProjectId} created in org {OrgId} by user {UserId}.",
            project.Id, organizationId, userId);

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
