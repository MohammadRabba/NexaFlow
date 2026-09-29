using MediatR;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Complete the password reset flow. The token must be valid (unexpired, unused)
///     and the new password must meet the password policy. On success, ALL refresh
///     tokens for the user are revoked (defense against stolen session tokens after
///     password reset).
/// </summary>
public sealed record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword) : IRequest<Unit>;
