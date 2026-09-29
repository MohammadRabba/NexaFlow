using MediatR;

namespace NexaFlow.Application.Features.Members.Commands;

/// <summary>
///     Transfer ownership to an existing member. The current Owner becomes an Admin.
///     Only the current Owner can call this (the controller's [Authorize(Policy = Permissions.MemberTransferOwnership)]
///     guarantees the actor holds a role that grants transfer_ownership — only Owner does).
/// </summary>
public sealed record TransferOwnershipCommand(
    Guid OrganizationId,
    Guid ToUserId) : IRequest<Unit>;
