using MediatR;
using NexaFlow.Application.Features.Members.Dtos;
using NexaFlow.Domain.Enums;

namespace NexaFlow.Application.Features.Members.Commands;

/// <summary>
///     Invite a user to an organization by email. The user must already exist in the system
///     (Phase 3 does NOT support email-based invitations to non-registered users — that
///     would require an out-of-band signup token, deferred). The invitation creates a
///     pending OrganizationMember (IsActive=false) with a hashed token; the plaintext
///     token is returned to the inviter ONCE.
/// </summary>
public sealed record InviteMemberCommand(
    Guid OrganizationId,
    string InviteeEmail,
    OrganizationRole Role) : IRequest<InviteSummaryDto>;
