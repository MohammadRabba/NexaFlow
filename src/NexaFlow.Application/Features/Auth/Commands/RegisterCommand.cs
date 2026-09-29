using MediatR;
using NexaFlow.Application.Features.Auth.Dtos;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Register a new user. The user is created with EmailVerified=false and an
///     email verification token is issued (hashed, never persisted in plaintext).
/// </summary>
public sealed record RegisterCommand(
    string Email,
    string DisplayName,
    string Password) : IRequest<AuthResponse>;
