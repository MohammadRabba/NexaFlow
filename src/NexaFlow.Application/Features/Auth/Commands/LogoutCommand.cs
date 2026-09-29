using MediatR;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Logout: revoke the presented refresh token. Access tokens remain valid until
///     they expire (15 minutes) — JWT is stateless. Client-side discard of the access
///     token is the recommended pattern; server-side refresh-token revocation is the
///     authoritative step.
/// </summary>
public sealed record LogoutCommand(
    string RefreshToken) : IRequest<Unit>;
