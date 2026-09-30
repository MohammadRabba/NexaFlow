using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Tasks.Commands;

public sealed class UpdateTaskCommandHandler : IRequestHandler<UpdateTaskCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;
    private readonly ILogger<UpdateTaskCommandHandler> _logger;

    public UpdateTaskCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess projectAccess,
        IAuditService audit,
        ILogger<UpdateTaskCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _projectAccess = projectAccess;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Unit> Handle(UpdateTaskCommand request, CancellationToken cancellationToken)
    {
        var project = await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);
        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var userId = _currentUser.UserId!.Value;
        task.Update(
            request.NewTitle, request.NewDescription, request.NewPriority,
            request.DueDateUtc, request.UpdateDueDate, userId, DateTimeOffset.UtcNow);

        // Audit TaskUpdated — same transaction. Payload records which fields changed
        // (no secrets — task fields are business data).
        await _audit.RecordAsync(
            action: AuditAction.TaskUpdated,
            entity: "Task",
            entityId: task.Id,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Task {TaskId} updated by user {UserId}.", task.Id, userId);
        return Unit.Value;
    }
}

public sealed class ChangeTaskStatusCommandHandler : IRequestHandler<ChangeTaskStatusCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;

    public ChangeTaskStatusCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess projectAccess,
        IAuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _projectAccess = projectAccess;
        _audit = audit;
    }

    public async Task<Unit> Handle(ChangeTaskStatusCommand request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);
        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var oldStatus = task.Status;
        task.ChangeStatus(request.NewStatus, _currentUser.UserId, DateTimeOffset.UtcNow);

        // Audit TaskStatusChanged — same transaction. Records from→to for state-machine forensics.
        await _audit.RecordAsync(
            action: AuditAction.TaskStatusChanged,
            entity: "Task",
            entityId: task.Id,
            oldValues: $"{{\"status\":\"{oldStatus}\"}}",
            newValues: $"{{\"status\":\"{request.NewStatus}\"}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed class AssignTaskCommandHandler : IRequestHandler<AssignTaskCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;

    public AssignTaskCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess projectAccess,
        IAuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _projectAccess = projectAccess;
        _audit = audit;
    }

    public async Task<Unit> Handle(AssignTaskCommand request, CancellationToken cancellationToken)
    {
        var project = await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);
        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        if (request.AssigneeId.HasValue)
        {
            var isMember = project.Members.Any(m => m.UserId == request.AssigneeId.Value);
            if (!isMember)
                throw new DomainException(
                    "Assignee must be a member of this project.",
                    "ASSIGNEE_NOT_PROJECT_MEMBER");
        }

        task.Assign(request.AssigneeId, _currentUser.UserId, DateTimeOffset.UtcNow);

        // Audit TaskUpdated (assignment change). Reuses TaskUpdated action — an assignment
        // change is conceptually an update. The new assignee is recorded.
        await _audit.RecordAsync(
            action: AuditAction.TaskUpdated,
            entity: "Task",
            entityId: task.Id,
            newValues: $"{{\"assigneeId\":\"{request.AssigneeId}\"}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed class DeleteTaskCommandHandler : IRequestHandler<DeleteTaskCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;
    private readonly ILogger<DeleteTaskCommandHandler> _logger;

    public DeleteTaskCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess projectAccess,
        IAuditService audit,
        ILogger<DeleteTaskCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _projectAccess = projectAccess;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Unit> Handle(DeleteTaskCommand request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);
        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var userId = _currentUser.UserId!.Value;
        task.SoftDelete(userId, DateTimeOffset.UtcNow);

        // Audit TaskDeleted — same transaction. Records the soft-delete event.
        await _audit.RecordAsync(
            action: AuditAction.TaskDeleted,
            entity: "Task",
            entityId: task.Id,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Task {TaskId} soft-deleted by user {UserId}.", task.Id, userId);
        return Unit.Value;
    }
}
