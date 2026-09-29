using MediatR;
using NexaFlow.Application.Features.Auth.Dtos;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Rotate a refresh token: present the old refresh token, receive a new access
///     token and a new (rotated) refresh token. The old refresh token is revoked.
///     If a revoked token is presented, the entire family is revoked (reuse detection).
/// </summary>
public sealed record RefreshCommand(
    string RefreshToken,
    string? IpAddress = null) : IRequest<AuthResponse>;
