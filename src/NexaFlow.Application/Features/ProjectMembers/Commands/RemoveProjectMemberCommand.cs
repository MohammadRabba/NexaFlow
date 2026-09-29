using MediatR;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

/// <summary>
///     Remove a member from a project. Hard-deletes the membership row (audit logs
/// reference user_id + project_id, not the membership row). The project Owner cannot
/// be removed via this command — must transfer ownership first. Self-removal is
/// allowed for non-Owners (leave voluntarily).
/// </summary>
public sealed record RemoveProjectMemberCommand(
    Guid ProjectId,
    Guid TargetUserId) : IRequest<Unit>;
