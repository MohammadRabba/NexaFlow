using MediatR;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Change the password for the currently-authenticated user. Requires the
///     old password for verification (defense against stolen access tokens being
///     used to lock out the real owner). On success, refresh tokens issued BEFORE
///     the password change are revoked (sessions on other devices are logged out).
/// </summary>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : IRequest<Unit>;
