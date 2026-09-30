using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Events.Tasks;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Labels.Commands;

public sealed record CreateLabelCommand(string Name, string? Color) : IRequest<Label>;
public sealed record DeleteLabelCommand(Guid LabelId) : IRequest<Unit>;
public sealed record AttachLabelCommand(Guid ProjectId, Guid TaskId, Guid LabelId) : IRequest<Unit>;
public sealed record DetachLabelCommand(Guid ProjectId, Guid TaskId, Guid LabelId) : IRequest<Unit>;

public sealed class LabelCommandHandlers :
    IRequestHandler<CreateLabelCommand, Label>,
    IRequestHandler<DeleteLabelCommand, Unit>,
    IRequestHandler<AttachLabelCommand, Unit>,
    IRequestHandler<DetachLabelCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;

    public LabelCommandHandlers(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ProjectAccess projectAccess,
        IAuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _projectAccess = projectAccess;
        _audit = audit;
    }

    public async Task<Label> Handle(CreateLabelCommand request, CancellationToken cancellationToken)
    {
        var orgId = _currentTenant.RequireTenantId();
        var label = Label.Create(orgId, request.Name, request.Color, DateTimeOffset.UtcNow);
        _db.Add(label);

        // Audit LabelCreated — same transaction.
        await _audit.RecordAsync(
            action: AuditAction.LabelCreated,
            entity: "Label",
            entityId: label.Id,
            newValues: $"{{\"name\":{System.Text.Json.JsonSerializer.Serialize(label.Name)},\"color\":{System.Text.Json.JsonSerializer.Serialize(label.Color ?? string.Empty)}}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return label;
    }

    public async Task<Unit> Handle(DeleteLabelCommand request, CancellationToken cancellationToken)
    {
        var orgId = _currentTenant.RequireTenantId();
        var label = await _db.FindLabelAsync(request.LabelId, orgId, cancellationToken)
            ?? throw new NotFoundException("Label", request.LabelId);
        _db.Remove(label);

        // Audit LabelDeleted — same transaction.
        await _audit.RecordAsync(
            action: AuditAction.LabelDeleted,
            entity: "Label",
            entityId: label.Id,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(AttachLabelCommand request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var orgId = _currentTenant.RequireTenantId();
        var label = await _db.FindLabelAsync(request.LabelId, orgId, cancellationToken)
            ?? throw new NotFoundException("Label", request.LabelId);

        var taskLabel = TaskLabel.Create(task.Id, label.Id, orgId, DateTimeOffset.UtcNow);
        _db.Add(taskLabel);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        when (ex.InnerException?.Message?.Contains("uq_task_labels") == true)
        {
            throw new DomainException("This label is already attached to this task.",
                "LABEL_ALREADY_ATTACHED");
        }

        return Unit.Value;
    }

    public async Task<Unit> Handle(DetachLabelCommand request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var orgId = _currentTenant.RequireTenantId();
        var taskLabel = await _db.FindTaskLabelAsync(
            request.TaskId, request.LabelId, orgId, cancellationToken)
            ?? throw new NotFoundException("TaskLabel", (request.TaskId, request.LabelId));

        _db.Remove(taskLabel);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
