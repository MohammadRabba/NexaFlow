using MediatR;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Members.Commands;

/// <summary>
///     Change a member's role. The target's current role determines what's allowed:
///     <list type="bullet">
///         <item>If target is Owner: rejected (use <see cref="TransferOwnershipCommand" />).</item>
///         <item>If target is Admin: only Owner can change their role (Admin cannot modify Admin).</item>
///         <item>If target is Member/Viewer: Owner OR Admin can change.</item>
///     </list>
///     These rules are enforced in the handler (authorization layer), not the domain.
/// </summary>
public sealed record UpdateMemberRoleCommand(
    Guid OrganizationId,
    Guid TargetUserId,
    OrganizationRole NewRole) : IRequest<Unit>;
