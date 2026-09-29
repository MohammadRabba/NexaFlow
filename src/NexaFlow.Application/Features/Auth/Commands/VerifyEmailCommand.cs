using MediatR;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Verify the user's email using the one-time token issued at registration or
///     re-issued via the forgot-password endpoint. Single-use.
/// </summary>
public sealed record VerifyEmailCommand(
    string Email,
    string Token) : IRequest<Unit>;
