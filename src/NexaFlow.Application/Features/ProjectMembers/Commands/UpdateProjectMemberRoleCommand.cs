using MediatR;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

/// <summary>
///     Change a project member's role. Authorization (enforced in the handler):
/// - Project Owner can change anyone's role (except another Owner's — use transfer-ownership).
/// - Contributor / Reader cannot change roles.
/// </summary>
public sealed record UpdateProjectMemberRoleCommand(
    Guid ProjectId,
    Guid TargetUserId,
    ProjectMemberRole NewRole) : IRequest<Unit>;
