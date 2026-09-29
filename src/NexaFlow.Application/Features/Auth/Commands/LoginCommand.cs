using MediatR;
using NexaFlow.Application.Features.Auth.Dtos;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Authenticate with email + password. Issues a new access token + a fresh refresh
///     token (start of a new family). Failed-login counters are incremented; the account
///     is locked out after the configured threshold.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? IpAddress = null) : IRequest<AuthResponse>;
