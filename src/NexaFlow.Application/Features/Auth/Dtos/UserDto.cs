namespace NexaFlow.Application.Features.Auth.Dtos;

/// <summary>
///     User representation for caching. Contains only non-sensitive fields.
///     Never cache password hashes, tokens, or other security material.
/// </summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified);
