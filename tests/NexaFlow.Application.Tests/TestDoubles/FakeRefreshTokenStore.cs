using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     Test double for <see cref="IRefreshTokenStore" />. Used by Application-layer
///     unit tests so they don't depend on EF Core / Postgres.
///     <para>
///         Implements rotation + reuse detection using in-memory state, mirroring the
///         production RefreshTokenStore's behavior so handler tests can assert on the
///         same security invariants.
///     </para>
/// </summary>
public sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    private readonly ISecureTokenGenerator _tokenGenerator;
    public List<RefreshToken> Tokens { get; } = [];

    public FakeRefreshTokenStore(ISecureTokenGenerator tokenGenerator)
    {
        _tokenGenerator = tokenGenerator;
    }

    public Task<IssuedRefreshToken> IssueNewAsync(
        Guid userId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset atUtc,
        string? createdFromIp,
        string? createdByUserAgent,
        CancellationToken cancellationToken = default)
    {
        var (plaintext, hash) = _tokenGenerator.Generate();
        var token = RefreshToken.CreateNew(
            userId: userId,
            tokenHash: hash,
            expiresAtUtc: expiresAtUtc,
            createdAtUtc: atUtc,
            createdFromIp: createdFromIp,
            createdByUserAgent: createdByUserAgent);
        Tokens.Add(token);
        return Task.FromResult(new IssuedRefreshToken(token, plaintext));
    }

    public Task<IssuedRefreshToken?> RotateAsync(
        string presentedTokenPlaintext,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset atUtc,
        string? presentedFromIp,
        string? presentedByUserAgent,
        CancellationToken cancellationToken = default)
    {
        var presentedHash = _tokenGenerator.HashPlaintext(presentedTokenPlaintext);
        var presented = Tokens.FirstOrDefault(t => t.TokenHash == presentedHash);

        if (presented is null)
        {
            return Task.FromResult<IssuedRefreshToken?>(null);
        }

        if (presented.IsExpired)
        {
            return Task.FromResult<IssuedRefreshToken?>(null);
        }

        if (presented.IsRevoked)
        {
            // REUSE DETECTED — revoke the entire family.
            foreach (var t in Tokens.Where(t => t.FamilyId == presented.FamilyId && !t.IsRevoked))
            {
                t.Revoke("REUSE_DETECTED", atUtc);
            }
            throw new RefreshTokenReuseException(presented.UserId, presented.FamilyId);
        }

        var (newPlaintext, newHash) = _tokenGenerator.Generate();
        var newToken = RefreshToken.CreateRotation(
            userId: presented.UserId,
            tokenHash: newHash,
            familyId: presented.FamilyId,
            expiresAtUtc: expiresAtUtc,
            createdAtUtc: atUtc,
            createdFromIp: presentedFromIp,
            createdByUserAgent: presentedByUserAgent);
        Tokens.Add(newToken);
        presented.Rotate(newToken, atUtc);
        return Task.FromResult<IssuedRefreshToken?>(new IssuedRefreshToken(newToken, newPlaintext));
    }

    public Task<bool> RevokeAsync(
        string presentedTokenPlaintext,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        var hash = _tokenGenerator.HashPlaintext(presentedTokenPlaintext);
        var token = Tokens.FirstOrDefault(t => t.TokenHash == hash);
        if (token is null || token.IsRevoked)
        {
            return Task.FromResult(false);
        }
        token.Revoke(reason, atUtc);
        return Task.FromResult(true);
    }

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        var activeTokens = Tokens.Where(t => t.UserId == userId && !t.IsRevoked).ToList();
        foreach (var t in activeTokens) t.Revoke(reason, atUtc);
        return Task.FromResult(activeTokens.Count);
    }

    public Task<int> RevokeTokensForUserOlderThanAsync(
        Guid userId,
        DateTimeOffset cutoffUtc,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        var tokensToRevoke = Tokens
            .Where(t => t.UserId == userId && !t.IsRevoked && t.CreatedAtUtc < cutoffUtc)
            .ToList();
        foreach (var t in tokensToRevoke) t.Revoke(reason, atUtc);
        return Task.FromResult(tokensToRevoke.Count);
    }
}
