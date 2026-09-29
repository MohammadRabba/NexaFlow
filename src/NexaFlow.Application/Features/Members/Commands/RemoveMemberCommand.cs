using MediatR;

namespace NexaFlow.Application.Features.Members.Commands;

/// <summary>
///     Remove a member. The Owner cannot be removed via this command — ownership must be
///     transferred first (use <see cref="TransferOwnershipCommand" />). Self-removal is
///     allowed for non-Owners (a user can leave an organization voluntarily).
/// </summary>
public sealed record RemoveMemberCommand(
    Guid OrganizationId,
    Guid TargetUserId) : IRequest<Unit>;
