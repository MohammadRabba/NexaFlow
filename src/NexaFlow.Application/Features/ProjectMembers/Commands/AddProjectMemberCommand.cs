using MediatR;
using NexaFlow.Application.Features.ProjectMembers.Dtos;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

/// <summary>
///     Add a member to a project. The invitee must already be a registered user (we don't
///     support email-based invites to non-registered users in Phase 4). Authorization:
/// only project Owners can add members (minimumRole=Owner, enforced by the handler).
/// </summary>
public sealed record AddProjectMemberCommand(
    Guid ProjectId,
    Guid UserId,
    ProjectMemberRole Role) : IRequest<ProjectMemberDto>;
