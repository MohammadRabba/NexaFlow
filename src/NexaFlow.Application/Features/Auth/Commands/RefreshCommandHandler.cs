using MediatR;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Handles <see cref="RefreshCommand" />:
///     <list type="number">
///         <item>Ask <c>IRefreshTokenStore</c> to rotate the token.</item>
///         <item>If the token is unknown or expired: 401 <c>INVALID_REFRESH_TOKEN</c>.</item>
///         <item>If reuse is detected: the store throws <c>RefreshTokenReuseException</c>
///             and has already revoked the entire family; we propagate that to the controller
///             (which logs a security event and returns 401).</item>
///         <item>If the user no longer exists or is locked out: revoke the family, 401.</item>
///         <item>Issue a new short-lived access token.</item>
///     </list>
/// </summary>
public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, AuthResponse>
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IAuditService _audit;
    private readonly AuthOptions _options;

    public RefreshCommandHandler(
        IApplicationDbContext db,
        IJwtTokenService jwtTokenService,
        IRefreshTokenStore refreshTokenStore,
        IAuditService audit,
        IOptions<AuthOptions> options)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _refreshTokenStore = refreshTokenStore;
        _audit = audit;
        _options = options.Value;
    }

    public async Task<AuthResponse> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;

        // RotateAsync performs: lookup by hash, validate active, issue new token,
        // mark old as revoked-and-replaced, OR detect reuse + revoke family + throw.
        var rotated = await _refreshTokenStore.RotateAsync(
            presentedTokenPlaintext: request.RefreshToken,
            expiresAtUtc: now + _options.RefreshTokenLifetime,
            atUtc: now,
            presentedFromIp: request.IpAddress,
            presentedByUserAgent: null,
            cancellationToken);

        if (rotated is null)
        {
            // Either the token doesn't exist or it's already expired.
            // Section 8: do not reveal which — generic 401.
            throw new DomainException(
                "Invalid or expired refresh token.",
                "INVALID_REFRESH_TOKEN");
        }

        var refreshToken = rotated.TokenEntity;

        // Load the user (we must NOT issue tokens for users who have been locked out or
        // whose password has changed since the refresh token was issued).
        var user = await _db.FindUserByIdAsync(refreshToken.UserId, cancellationToken);

        if (user is null)
        {
            // The user was deleted between token issuance and refresh. Revoke the token family.
            await _refreshTokenStore.RevokeAllForUserAsync(
                userId: refreshToken.UserId,
                reason: "USER_DELETED",
                atUtc: now,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "Invalid or expired refresh token.",
                "INVALID_REFRESH_TOKEN");
        }

        if (user.IsLockedOut)
        {
            await _refreshTokenStore.RevokeAllForUserAsync(
                userId: user.Id,
                reason: "ACCOUNT_LOCKED",
                atUtc: now,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "Invalid or expired refresh token.",
                "INVALID_REFRESH_TOKEN");
        }

        // Defense-in-depth: if the user's password changed AFTER this refresh token was issued,
        // reject the refresh and revoke the family. The store SHOULD have already done this in
        // RevokeTokensForUserOlderThanAsync during password-change, but a defensive check costs
        // nothing.
        if (user.PasswordChangedAtUtc is { } passwordChangedAt
            && refreshToken.CreatedAtUtc < passwordChangedAt)
        {
            await _refreshTokenStore.RevokeAllForUserAsync(
                userId: user.Id,
                reason: "PASSWORD_CHANGED_AFTER_ISSUANCE",
                atUtc: now,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "Invalid or expired refresh token.",
                "INVALID_REFRESH_TOKEN");
        }

        // Commit the rotation + any of the above revocations.
        // Audit RefreshTokenRotated — same transaction as the rotation. Records that a new
        // refresh token was issued (replaces an old one); useful for detecting session
        // hijacking patterns. No token payloads are recorded.
        await _audit.RecordAsync(
            action: AuditAction.RefreshTokenRotated,
            entity: "RefreshToken",
            entityId: refreshToken.Id,
            actorUserIdOverride: user.Id,
            organizationIdOverride: null,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user, now);
        var accessExpiry = now + _options.AccessTokenLifetime;

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: rotated.PlaintextToken,
            AccessTokenExpiresAtUtc: accessExpiry,
            RefreshTokenExpiresAtUtc: refreshToken.ExpiresAtUtc,
            User: new AuthUserDto(
                Id: user.Id,
                Email: user.Email.Value,
                DisplayName: user.DisplayName,
                EmailVerified: user.EmailVerified));
    }
}
