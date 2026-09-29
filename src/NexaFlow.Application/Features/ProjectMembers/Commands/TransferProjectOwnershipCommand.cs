using MediatR;

namespace NexaFlow.Application.Features.ProjectMembers.Commands;

/// <summary>
///     Transfer project ownership to an existing member. The current Owner becomes a Contributor.
/// Only the current Owner can call (minimumRole=Owner, verified by ProjectAccess).
/// </summary>
public sealed record TransferProjectOwnershipCommand(
    Guid ProjectId,
    Guid ToUserId) : IRequest<Unit>;
