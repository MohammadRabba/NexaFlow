using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     Factory for an <see cref="AuthOptions" /> instance with test-default values.
///     Phase 2 tests do NOT validate the AuthOptions validation rules — they assert
///     on the runtime behavior of handlers with a known configuration.
/// </summary>
public static class TestAuthOptions
{
    public static IOptions<AuthOptions> Default => Options.Create(new AuthOptions
    {
        JwtSigningKey = "test-signing-key-256-bits-DO-NOT-USE-IN-PROD-12345678",
        JwtIssuer = "https://test.nexaflow.com",
        JwtAudience = "test-clients",
        AccessTokenLifetime = TimeSpan.FromMinutes(15),
        RefreshTokenLifetime = TimeSpan.FromDays(7),
        EmailVerificationTokenLifetime = TimeSpan.FromHours(24),
        PasswordResetTokenLifetime = TimeSpan.FromHours(1),
        MaxFailedLoginAttempts = 5,
        LockoutDuration = TimeSpan.FromMinutes(15),
        PasswordMinLength = 12,
        PasswordMaxLength = 128
    });
}
