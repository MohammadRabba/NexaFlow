using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Application.Features.Tasks.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Tasks.Commands;

public sealed class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, TaskDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;
    private readonly ILogger<CreateTaskCommandHandler> _logger;

    public CreateTaskCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ProjectAccess projectAccess,
        IAuditService audit,
        ILogger<CreateTaskCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _projectAccess = projectAccess;
        _audit = audit;
        _logger = logger;
    }

    public async Task<TaskDto> Handle(CreateTaskCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Resource-level: verify the user is a project Contributor (can create tasks).
        var project = await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Contributor, cancellationToken);

        var userId = _currentUser.UserId!.Value;
        var now = DateTimeOffset.UtcNow;

        // If an assignee is specified, they must be a project member (not just an org member).
        // An org member without project membership would get 404 on any task request — they
        // can't view the task they were "assigned" to. So we enforce: assignee must be a
        // project member.
        if (request.AssigneeId.HasValue)
        {
            var assigneeIsMember = project.Members.Any(m => m.UserId == request.AssigneeId.Value);
            if (!assigneeIsMember)
                throw new DomainException(
                    "Assignee must be a member of this project.",
                    "ASSIGNEE_NOT_PROJECT_MEMBER");
        }

        var task = TaskItem.Create(
            projectId: project.Id,
            organizationId: project.OrganizationId,
            title: request.Title,
            description: request.Description ?? string.Empty,
            reporterId: userId,  // server-derived, never from client
            priority: request.Priority,
            dueDateUtc: request.DueDateUtc,
            atUtc: now);

        _db.Add(task);

        // Audit TaskCreated — same transaction as the task row.
        await _audit.RecordAsync(
            action: AuditAction.TaskCreated,
            entity: "Task",
            entityId: task.Id,
            newValues: $"{{\"projectId\":\"{task.ProjectId}\",\"title\":{System.Text.Json.JsonSerializer.Serialize(task.Title)},\"priority\":\"{task.Priority}\"}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Task {TaskId} created in project {ProjectId} by user {UserId}.",
            task.Id, project.Id, userId);

        return new TaskDto(
            Id: task.Id,
            ProjectId: task.ProjectId,
            Title: task.Title,
            Description: task.Description,
            Status: task.Status,
            Priority: task.Priority,
            AssigneeId: task.AssigneeId,
            ReporterId: task.ReporterId,
            DueDateUtc: task.DueDateUtc,
            CreatedAtUtc: task.CreatedAtUtc,
            UpdatedAtUtc: task.UpdatedAtUtc);
    }
}
