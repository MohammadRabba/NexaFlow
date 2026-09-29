namespace NexaFlow.Application.Features.Auth.Dtos;

/// <summary>
///     Successful authentication response. The access token is short-lived (15 min);
///     the refresh token is one-time-use and must be rotated on the next refresh.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthUserDto User);

public sealed record AuthUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified);
