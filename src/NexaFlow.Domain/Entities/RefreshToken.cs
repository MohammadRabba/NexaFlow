using NexaFlow.Domain.Common;

namespace NexaFlow.Domain.Entities;

/// <summary>
///     A refresh-token aggregate (Phase 2 — Authentication). Refresh tokens are
///     user-scoped (not tenant-scoped) — a refresh token grants access to all
///     organizations the user belongs to.
///     <para>
///         Security invariants (section 8):
///         <list type="bullet">
///             <item>Only the hash is stored — never the plaintext token.</item>
///             <item>Tokens are single-use: rotation marks the old token RevokedAt + ReplacedBy.</item>
///             <item>Family detection: when a token that is already Revoked is presented again, the entire family is revoked — defense against refresh-token theft replay.</item>
///             <item>Tokens expire (7 days default) regardless of revocation.</item>
///         </list>
///     </para>
/// </summary>
public class RefreshToken : AggregateRoot
{
    private RefreshToken() { }

    /// <summary>The user this token grants access to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    ///     SHA-256 hash of the plaintext token. Never store the plaintext token.
    ///     Indexed unique — there's only ever one active token with a given hash.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    ///     Group identifier — tokens issued from the same login share a family id.
    ///     Used for replay detection: if a revoked token is presented again,
    ///     we revoke the entire family.
    /// </summary>
    public Guid FamilyId { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Null while the token is active. Set when the token is revoked (rotated or explicitly logged out).</summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>Null while the token is active. Set when the token is rotated into a new one.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Null while the token is active. Set when the token is explicitly revoked (logout) without rotation.</summary>
    public string? RevocationReason { get; private set; }

    /// <summary>IP from which the token was issued (audit).</summary>
    public string? CreatedFromIp { get; private set; }

    /// <summary>User-Agent from which the token was issued (audit).</summary>
    public string? CreatedByUserAgent { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsActive => !IsExpired && !IsRevoked;

    // --- Factory ---

    /// <summary>
    ///     Create a new refresh token as the start of a new family (i.e., from login).
    ///     The plaintext token is never stored; only its SHA-256 hash is.
    /// </summary>
    public static RefreshToken CreateNew(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? createdFromIp,
        string? createdByUserAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (userId == Guid.Empty) throw new ArgumentException("UserId must not be empty.", nameof(userId));

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            // A new family starts at login; rotations within the same family keep the same FamilyId.
            FamilyId = Guid.NewGuid(),
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            CreatedFromIp = createdFromIp,
            CreatedByUserAgent = createdByUserAgent
        };
    }

    /// <summary>
    ///     Create a new refresh token as a rotation of an existing active token.
    ///     The new token shares the <paramref name="familyId" /> with its predecessor —
    ///     this is what enables family-wide revocation on reuse detection.
    /// </summary>
    public static RefreshToken CreateRotation(
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? createdFromIp,
        string? createdByUserAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (userId == Guid.Empty) throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (familyId == Guid.Empty) throw new ArgumentException("FamilyId must not be empty.", nameof(familyId));

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            CreatedFromIp = createdFromIp,
            CreatedByUserAgent = createdByUserAgent
        };
    }

    // --- Domain mutations ---

    /// <summary>
    ///     Mark this token as rotated into <paramref name="replacement" />. The token
    ///     becomes revoked at <paramref name="atUtc" />; <paramref name="replacement" />
    ///     takes over as the active token in the family.
    /// </summary>
    public void Rotate(RefreshToken replacement, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.FamilyId != FamilyId)
            throw new InvalidOperationException("Replacement token must belong to the same family.");
        if (replacement.Id == Id)
            throw new InvalidOperationException("Replacement token must be a different entity.");
        if (!IsActive)
            throw new InvalidOperationException("Cannot rotate a token that is already revoked or expired.");

        RevokedAtUtc = atUtc;
        ReplacedByTokenId = replacement.Id;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    ///     Explicitly revoke this token (e.g., on logout, password change, reuse detection).
    ///     Distinct from <see cref="Rotate" />: no replacement is registered.
    /// </summary>
    public void Revoke(string reason, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (IsRevoked) return; // Idempotent
        RevokedAtUtc = atUtc;
        RevocationReason = reason;
        UpdatedAtUtc = atUtc;
    }
}
