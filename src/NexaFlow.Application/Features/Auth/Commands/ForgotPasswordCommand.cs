using MediatR;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Initiate the password reset flow. Always returns success — never reveal whether
///     an email exists in the system. If the email is registered and verified, a reset
///     token is generated (hashed only) and emailed.
/// </summary>
public sealed record ForgotPasswordCommand(
    string Email) : IRequest<Unit>;
