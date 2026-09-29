using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Events.Users;
using NexaFlow.Infrastructure.Persistence;

namespace NexaFlow.Infrastructure.Authentication;

/// <summary>
///     Manages refresh-token lifecycle against the EF Core DbContext.
///     <para>
///         Critical security guarantees (see docs/authentication.md, ADR-005):
///         <list type="bullet">
///             <item>Plaintext tokens are never persisted — only SHA-256 hashes (via <see cref="ISecureTokenGenerator" />).</item>
///             <item>Rotation: presenting token A issues token B and marks A revoked-and-replaced.</item>
///             <item>Reuse detection: presenting an already-revoked token revokes the ENTIRE family
///                 and throws <see cref="RefreshTokenReuseException" /> — defense against refresh-token theft replay.</item>
///         </list>
///     </para>
/// </summary>
public sealed class RefreshTokenStore : IRefreshTokenStore
{
    private readonly ApplicationDbContext _db;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ILogger<RefreshTokenStore> _logger;

    public RefreshTokenStore(
        ApplicationDbContext db,
        ISecureTokenGenerator tokenGenerator,
        ILogger<RefreshTokenStore> logger)
    {
        _db = db;
        _tokenGenerator = tokenGenerator;
        _logger = logger;
    }

    public async Task<IssuedRefreshToken> IssueNewAsync(
        Guid userId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset atUtc,
        string? createdFromIp,
        string? createdByUserAgent,
        CancellationToken cancellationToken = default)
    {
        var (plaintextToken, tokenHash) = _tokenGenerator.Generate();

        var refreshToken = RefreshToken.CreateNew(
            userId: userId,
            tokenHash: tokenHash,
            expiresAtUtc: expiresAtUtc,
            createdAtUtc: atUtc,
            createdFromIp: createdFromIp,
            createdByUserAgent: createdByUserAgent);

        _db.Add(refreshToken);
        // Caller's handler invokes SaveChangesAsync — staged changes persist together.

        return new IssuedRefreshToken(refreshToken, plaintextToken);
    }

    public async Task<IssuedRefreshToken?> RotateAsync(
        string presentedTokenPlaintext,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset atUtc,
        string? presentedFromIp,
        string? presentedByUserAgent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedTokenPlaintext);
        var presentedTokenHash = _tokenGenerator.HashPlaintext(presentedTokenPlaintext);

        // Find the presented token by its hash. The DB index on TokenHash is unique.
        var presentedToken = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == presentedTokenHash, cancellationToken);

        if (presentedToken is null)
        {
            // Token doesn't exist (likely malformed or already revoked-then-deleted in a future cleanup).
            _logger.LogInformation("Refresh rotation rejected: token hash not found in store.");
            return null;
        }

        if (presentedToken.IsExpired)
        {
            _logger.LogInformation(
                "Refresh rotation rejected: token {TokenId} expired at {ExpiryUtc}.",
                presentedToken.Id,
                presentedToken.ExpiresAtUtc);
            return null;
        }

        // --- REUSE DETECTION ---
        // The presented token has already been revoked (either rotated or explicitly revoked).
        // This is a strong signal of token theft / replay. Defense: revoke the ENTIRE family
        // so that all tokens issued from the same login session are invalidated simultaneously.
        if (presentedToken.IsRevoked)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} family {FamilyId} " +
                "(token {TokenId} already revoked). Revoking entire family.",
                presentedToken.UserId,
                presentedToken.FamilyId,
                presentedToken.Id);

            await RevokeFamilyAsync(presentedToken.UserId, presentedToken.FamilyId, "REUSE_DETECTED", atUtc, cancellationToken);

            presentedToken.AddDomainEvent(new RefreshTokenReuseDetectedEvent(
                UserId: presentedToken.UserId,
                TokenFamilyId: presentedToken.FamilyId,
                PresentedTokenId: presentedToken.Id,
                PresentedFromIp: presentedFromIp,
                OccurredOnUtc: atUtc));

            throw new RefreshTokenReuseException(presentedToken.UserId, presentedToken.FamilyId);
        }

        // Active + not expired — rotate.
        var (newPlaintext, newHash) = _tokenGenerator.Generate();
        var newToken = RefreshToken.CreateRotation(
            userId: presentedToken.UserId,
            tokenHash: newHash,
            familyId: presentedToken.FamilyId,  // rotation stays in the same family
            expiresAtUtc: expiresAtUtc,
            createdAtUtc: atUtc,
            createdFromIp: presentedFromIp,
            createdByUserAgent: presentedByUserAgent);

        _db.Add(newToken);

        // Mark the old token as rotated (revoked + replaced).
        presentedToken.Rotate(newToken, atUtc);

        return new IssuedRefreshToken(newToken, newPlaintext);
    }

    public async Task<bool> RevokeAsync(
        string presentedTokenPlaintext,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedTokenPlaintext);
        var presentedTokenHash = _tokenGenerator.HashPlaintext(presentedTokenPlaintext);

        var token = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == presentedTokenHash, cancellationToken);

        if (token is null || token.IsRevoked)
        {
            return false;
        }

        token.Revoke(reason, atUtc);
        return true;
    }

    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        // Active tokens are those not yet revoked (RevokedAtUtc is null).
        // Load them all and revoke individually — the domain method stamps the audit metadata.
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(reason, atUtc);
        }

        return activeTokens.Count;
    }

    public async Task<int> RevokeTokensForUserOlderThanAsync(
        Guid userId,
        DateTimeOffset cutoffUtc,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        var tokensToRevoke = await _db.RefreshTokens
            .Where(t => t.UserId == userId
                && t.RevokedAtUtc == null
                && t.CreatedAtUtc < cutoffUtc)
            .ToListAsync(cancellationToken);

        foreach (var token in tokensToRevoke)
        {
            token.Revoke(reason, atUtc);
        }

        return tokensToRevoke.Count;
    }

    private async Task RevokeFamilyAsync(
        Guid userId,
        Guid familyId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var familyTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in familyTokens)
        {
            token.Revoke(reason, atUtc);
        }

        _logger.LogWarning(
            "Revoked {Count} token(s) in family {FamilyId} for user {UserId} (reason: {Reason}).",
            familyTokens.Count,
            familyId,
            userId,
            reason);
    }
}
