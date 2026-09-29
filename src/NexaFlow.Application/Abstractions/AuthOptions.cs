namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Options for the auth pipeline. Loaded from <c>Authentication</c> config section.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>JWT signing key (HS256). Minimum 256 bits / 32 bytes. NEVER logged.</summary>
    public string JwtSigningKey { get; set; } = string.Empty;

    /// <summary>JWT issuer (this API).</summary>
    public string JwtIssuer { get; set; } = "https://api.nexaflow.com";

    /// <summary>JWT audience (clients).</summary>
    public string JwtAudience { get; set; } = "nexaflow-clients";

    /// <summary>Access token lifetime. Default 15 minutes (section 8 example).</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Refresh token lifetime. Default 7 days (section 8 example).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Email verification token lifetime. Default 24 hours.</summary>
    public TimeSpan EmailVerificationTokenLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Password reset token lifetime. Default 1 hour (short — sensitive operation).</summary>
    public TimeSpan PasswordResetTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Maximum failed login attempts before lockout.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>Lockout duration after threshold reached. Default 15 minutes.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     Password policy — minimum length. OWASP recommends 8+ for user-chosen passwords;
    ///     we use 12 as a stronger default.
    /// </summary>
    public int PasswordMinLength { get; set; } = 12;

    /// <summary>Maximum password length — defends against BCrypt 72-byte limit / DoS.</summary>
    public int PasswordMaxLength { get; set; } = 128;
}
