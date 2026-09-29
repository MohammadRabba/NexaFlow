using NexaFlow.Domain.Common;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A registered user. Owns identity, credentials, lockout, email verification,
///     and password reset state (section 8 — Authentication requirements).
///     <para>
///         Security invariants:
///         <list type="bullet">
///             <item>Password is stored as a BCrypt hash — never plaintext.</item>
///             <item>Refresh tokens live in their own table (RefreshToken aggregate).</item>
///             <item>Lockout state lives here so the User aggregate is the consistency boundary.</item>
///             <item>Email verification + password reset tokens are stored as SHA-256 hashes; the
///                 plaintext token is returned to the caller only at issue time and is never logged.</item>
///         </list>
///     </para>
/// </summary>
public class User : AuditableEntity
{
    // EF Core parameterless constructor
    private User() { }

    public Email Email { get; private set; } = null!;

    /// <summary>Display name shown in UI.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>BCrypt hash including salt and cost factor in the encoded string.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public bool EmailVerified { get; private set; }
    public DateTimeOffset? EmailVerifiedAtUtc { get; private set; }

    // --- Account lockout (section 8) ---
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public bool IsLockedOut => LockoutEndUtc is not null && LockoutEndUtc > DateTimeOffset.UtcNow;

    // --- Email verification token (single-use, hashed) ---
    /// <summary>
    ///     SHA-256 hash of the email verification token. Null when no verification
    ///     is in flight or after consumption. Plaintext token is returned to the caller
    ///     only at issue time; never logged; never persisted.
    /// </summary>
    public string? EmailVerificationTokenHash { get; private set; }

    public DateTimeOffset? EmailVerificationTokenExpiresAtUtc { get; private set; }

    // --- Password reset token (single-use, hashed) ---
    /// <summary>SHA-256 hash of the password reset token. Same lifecycle as EmailVerificationTokenHash.</summary>
    public string? PasswordResetTokenHash { get; private set; }

    public DateTimeOffset? PasswordResetTokenExpiresAtUtc { get; private set; }

    /// <summary>
    ///     Timestamp of the last password change. Used to invalidate refresh tokens
    ///     issued before the password was changed (defense against stale sessions).
    /// </summary>
    public DateTimeOffset? PasswordChangedAtUtc { get; private set; }

    /// <summary>
    ///     Factory for a brand-new user. Throws if the email is invalid (Domain rule).
    ///     Application-layer validation gives the friendlier error first (section 13).
    ///     Issues an email verification token (hashed) at creation; the plaintext token
    ///     must be sent to the user out-of-band (email).
    /// </summary>
    public static User Create(
        Email email,
        string displayName,
        string passwordHash,
        string? emailVerificationTokenHash,
        DateTimeOffset? emailVerificationTokenExpiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (displayName.Length is < 1 or > 100)
            throw new ArgumentException("Display name must be 1..100 characters.");

        // Hash and expiry must both be set or both be null.
        var hashIsAbsent = string.IsNullOrWhiteSpace(emailVerificationTokenHash);
        var expiryIsAbsent = emailVerificationTokenExpiresAtUtc is null;
        if (hashIsAbsent != expiryIsAbsent)
        {
            throw new ArgumentException(
                "Email verification token hash and expiry must both be set or both be null.");
        }

        return new User
        {
            Email = email,
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            EmailVerified = false,
            FailedLoginAttempts = 0,
            EmailVerificationTokenHash = emailVerificationTokenHash,
            EmailVerificationTokenExpiresAtUtc = emailVerificationTokenExpiresAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    // --- Authentication lifecycle methods ---

    /// <summary>Mark the user's email as verified. Idempotent. Clears the verification token hash + expiry.</summary>
    public void VerifyEmail(DateTimeOffset atUtc)
    {
        if (EmailVerified) return;
        EmailVerified = true;
        EmailVerifiedAtUtc = atUtc;
        EmailVerificationTokenHash = null;
        EmailVerificationTokenExpiresAtUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    ///     Issue a new email verification token. Invalidates any previously issued
    ///     verification token (single-use). The plaintext token is returned by the
    ///     caller — never stored.
    /// </summary>
    public void IssueEmailVerificationToken(string tokenHash, DateTimeOffset expiresAtUtc, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAtUtc <= atUtc)
            throw new ArgumentException("Email verification token must expire in the future.", nameof(expiresAtUtc));

        EmailVerificationTokenHash = tokenHash;
        EmailVerificationTokenExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    ///     Verify the email using a token hash that matches the stored hash.
    ///     Single-use: clears the token on success. Throws on mismatch or expiry.
    /// </summary>
    public bool ConsumeEmailVerificationToken(string tokenHash, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (EmailVerified) return true; // Idempotent
        if (EmailVerificationTokenHash is null) return false;
        if (EmailVerificationTokenExpiresAtUtc < atUtc) return false;
        if (!string.Equals(EmailVerificationTokenHash, tokenHash, StringComparison.Ordinal)) return false;

        VerifyEmail(atUtc);
        return true;
    }

    /// <summary>
    ///     Issue a new password reset token. Invalidates any previously issued one.
    ///     The plaintext token is returned by the caller — never stored.
    /// </summary>
    public void IssuePasswordResetToken(string tokenHash, DateTimeOffset expiresAtUtc, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (expiresAtUtc <= atUtc)
            throw new ArgumentException("Password reset token must expire in the future.", nameof(expiresAtUtc));

        PasswordResetTokenHash = tokenHash;
        PasswordResetTokenExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    ///     Consume a password reset token. On success, sets the new password hash and
    ///     clears the reset token. Single-use. Returns false on mismatch or expiry.
    /// </summary>
    public bool ConsumePasswordResetToken(
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        if (PasswordResetTokenHash is null) return false;
        if (PasswordResetTokenExpiresAtUtc < atUtc) return false;
        if (!string.Equals(PasswordResetTokenHash, tokenHash, StringComparison.Ordinal)) return false;

        PasswordHash = newPasswordHash;
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiresAtUtc = null;
        PasswordChangedAtUtc = atUtc;
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = atUtc;
        return true;
    }

    /// <summary>
    ///     Change the password (when the user is already authenticated, not via reset flow).
    ///     Updates <see cref="PasswordChangedAtUtc" /> so refresh tokens issued before
    ///     this moment can be invalidated by the refresh handler.
    /// </summary>
    public void ChangePassword(string newPasswordHash, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        PasswordChangedAtUtc = atUtc;
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Register a failed login attempt and possibly lock the account.</summary>
    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration, DateTimeOffset atUtc)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (lockoutDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lockoutDuration));

        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            LockoutEndUtc = atUtc + lockoutDuration;
        }
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Register a successful login. Resets the failure counter.</summary>
    public void RegisterSuccessfulLogin(DateTimeOffset atUtc)
    {
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Unlock the account manually (admin override).</summary>
    public void Unlock(DateTimeOffset atUtc)
    {
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Update the display name only.</summary>
    public void UpdateDisplayName(string newDisplayName, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newDisplayName);
        if (newDisplayName.Length is < 1 or > 100)
            throw new ArgumentException("Display name must be 1..100 characters.");
        DisplayName = newDisplayName.Trim();
        UpdatedAtUtc = atUtc;
    }
}
