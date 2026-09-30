using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Projects.Commands;

public sealed class DeleteProjectCommandHandler : IRequestHandler<DeleteProjectCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _access;
    private readonly ICacheService _cache;
    private readonly IAuditService _audit;
    private readonly ILogger<DeleteProjectCommandHandler> _logger;

    public DeleteProjectCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess access,
        ICacheService cache,
        IAuditService audit,
        ILogger<DeleteProjectCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _access = access;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Unit> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Only project Owners can delete — Contributors and Readers cannot.
        var project = await _access.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Owner, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var userId = _currentUser.UserId!.Value;
        project.SoftDelete(userId, now);

        // Audit ProjectDeleted — same transaction as the soft-delete.
        await _audit.RecordAsync(
            action: AuditAction.ProjectDeleted,
            entity: "Project",
            entityId: project.Id,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Phase 7: Invalidate project cache
        await _cache.RemoveAsync($"project:{project.Id}", cancellationToken);

        _logger.LogWarning(
            "Project {ProjectId} soft-deleted by user {UserId} at {AtUtc}. " +
            "Memberships retained for audit; future reads exclude this project.",
            project.Id, userId, now);
        return Unit.Value;
    }
}
