using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Manages the refresh-token lifecycle: issue, rotate, revoke, validate.
///     All operations persist the result via <c>IApplicationDbContext</c> in the
///     calling handler's transaction.
///     <para>
///         Reuse / replay detection (per user directive):
///         <list type="bullet">
///             <item>Client sends refresh token A → server issues refresh token B → A becomes invalid.</item>
///             <item>Client later tries A again → request must be rejected safely.</item>
///             <item>When a revoked token is presented, the ENTIRE family is revoked
///                 (defense against refresh-token theft replay).</item>
///         </list>
///     </para>
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>
    ///     Issue a brand-new refresh token (start of a new family) for <paramref name="userId" />.
    ///     Returns the persisted entity + the plaintext token (returned to client once).
    /// </summary>
    Task<IssuedRefreshToken> IssueNewAsync(
        Guid userId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset atUtc,
        string? createdFromIp,
        string? createdByUserAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Rotate a refresh token. Looks up the token by its hash; if it's active,
    ///     issues a new token in the same family and marks the old one as revoked-and-replaced.
    ///     If the presented token has ALREADY been revoked (reuse attempt), the entire
    ///     family is revoked and a <see cref="RefreshTokenReuseException" /> is thrown.
    ///     If the token is expired or not found, returns null — caller translates to 401.
    /// </summary>
    Task<IssuedRefreshToken?> RotateAsync(
        string presentedTokenPlaintext,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset atUtc,
        string? presentedFromIp,
        string? presentedByUserAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revoke a refresh token by its plaintext value (e.g., on logout). Idempotent.
    ///     Returns false if the token doesn't exist or is already revoked.
    /// </summary>
    Task<bool> RevokeAsync(
        string presentedTokenPlaintext,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revoke ALL active refresh tokens for a user (e.g., on password change).
    ///     Returns the count of tokens revoked. Idempotent.
    /// </summary>
    Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revoke refresh tokens issued BEFORE <paramref name="cutoffUtc" /> for the user.
    ///     Used by the password-change flow to invalidate sessions created before the
    ///     password was changed. Returns count of tokens revoked.
    /// </summary>
    Task<int> RevokeTokensForUserOlderThanAsync(
        Guid userId,
        DateTimeOffset cutoffUtc,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Thrown by <see cref="IRefreshTokenStore.RotateAsync" /> when a revoked token
///     is presented again — i.e., a likely replay / theft attempt. The store has
///     already revoked the entire token family by the time this is thrown; the
///     handler converts this to a 401 with a security log entry.
/// </summary>
public sealed class RefreshTokenReuseException : Exception
{
    public RefreshTokenReuseException(Guid userId, Guid familyId)
        : base("Refresh token reuse detected. The token family has been revoked.")
    {
        UserId = userId;
        FamilyId = familyId;
    }

    public Guid UserId { get; }
    public Guid FamilyId { get; }
}
