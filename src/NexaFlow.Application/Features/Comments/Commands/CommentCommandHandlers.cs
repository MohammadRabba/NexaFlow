using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Projects.Commands;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Enums;
using NexaFlow.Domain.Events.Tasks;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Comments.Commands;

public sealed record CreateCommentCommand(Guid ProjectId, Guid TaskId, string Body) : IRequest<Comment>;
public sealed record EditCommentCommand(Guid ProjectId, Guid TaskId, Guid CommentId, string NewBody) : IRequest<Unit>;
public sealed record DeleteCommentCommand(Guid ProjectId, Guid TaskId, Guid CommentId) : IRequest<Unit>;

public sealed class CommentCommandHandlers :
    IRequestHandler<CreateCommentCommand, Comment>,
    IRequestHandler<EditCommentCommand, Unit>,
    IRequestHandler<DeleteCommentCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;
    private readonly ProjectAccess _projectAccess;
    private readonly IAuditService _audit;
    private readonly ILogger<CommentCommandHandlers> _logger;

    public CommentCommandHandlers(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant,
        ProjectAccess projectAccess,
        IAuditService audit,
        ILogger<CommentCommandHandlers> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _projectAccess = projectAccess;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Comment> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var project = await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var userId = _currentUser.UserId!.Value;
        var comment = Comment.Create(
            task.Id, project.OrganizationId, userId, request.Body, DateTimeOffset.UtcNow);

        comment.AddDomainEvent(new TaskCommentAddedEvent(Guid.NewGuid(),
            task.Id, comment.Id, userId, DateTimeOffset.UtcNow));

        _db.Add(comment);

        // Audit CommentCreated — same transaction as the comment row.
        await _audit.RecordAsync(
            action: AuditAction.CommentCreated,
            entity: "Comment",
            entityId: comment.Id,
            newValues: $"{{\"taskId\":\"{comment.TaskId}\",\"authorId\":\"{comment.AuthorId}\"}}",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return comment;
    }

    public async Task<Unit> Handle(EditCommentCommand request, CancellationToken cancellationToken)
    {
        await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var orgId = _currentTenant.RequireTenantId();
        var comment = await _db.FindCommentAsync(request.CommentId, orgId, cancellationToken)
            ?? throw new NotFoundException("Comment", request.CommentId);

        // Only the author can edit their own comment (section 18).
        if (comment.AuthorId != _currentUser.UserId)
            throw new DomainException("Only the comment author can edit.", "NOT_COMMENT_AUTHOR");

        comment.Edit(request.NewBody, _currentUser.UserId, DateTimeOffset.UtcNow);

        // Audit CommentUpdated — same transaction.
        await _audit.RecordAsync(
            action: AuditAction.CommentUpdated,
            entity: "Comment",
            entityId: comment.Id,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var project = await _projectAccess.LoadAndAuthorizeAsync(
            request.ProjectId, ProjectMemberRole.Reader, cancellationToken);

        var task = await _db.FindTaskAsync(request.TaskId, cancellationToken)
            ?? throw new NotFoundException("Task", request.TaskId);
        if (task.ProjectId != request.ProjectId)
            throw new NotFoundException("Task", request.TaskId);

        var orgId = _currentTenant.RequireTenantId();
        var comment = await _db.FindCommentAsync(request.CommentId, orgId, cancellationToken)
            ?? throw new NotFoundException("Comment", request.CommentId);

        // Author or project Owner can delete (section 18: "Only authorized users").
        var userId = _currentUser.UserId!.Value;
        if (comment.AuthorId != userId)
        {
            // Check if the user is the project Owner.
            var isProjectOwner = project.Members.Any(m =>
                m.UserId == userId && m.Role == ProjectMemberRole.Owner);
            if (!isProjectOwner)
                throw new DomainException(
                    "Only the comment author or a project Owner can delete.",
                    "NOT_AUTHORIZED_TO_DELETE_COMMENT");
        }

        comment.SoftDelete(userId, DateTimeOffset.UtcNow);

        // Audit CommentDeleted — same transaction.
        await _audit.RecordAsync(
            action: AuditAction.CommentDeleted,
            entity: "Comment",
            entityId: comment.Id,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
