using MediatR;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Auth.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Handles <see cref="LoginCommand" />:
///     <list type="number">
///         <item>Look up the user by normalized email.</item>
///         <item>If not found: throw a generic <c>INVALID_CREDENTIALS</c> exception (no enumeration leak).</item>
///         <item>If found but locked out: throw <c>ACCOUNT_LOCKED</c>.</item>
///         <item>If found but email not verified: throw <c>EMAIL_NOT_VERIFIED</c>.</item>
///         <item>Verify the password. On failure, register a failed login attempt; possibly lock; throw.</item>
///         <item>On success, reset the failed-login counter, issue access + refresh tokens.</item>
///     </list>
/// </summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    // The generic auth-failure error message. Identical for "user not found" and "wrong password" —
    // section 8: do not leak whether an email/account exists through authentication error messages.
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IAuditService _audit;
    private readonly AuthOptions _options;

    public LoginCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IRefreshTokenStore refreshTokenStore,
        IAuditService audit,
        IOptions<AuthOptions> options)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _refreshTokenStore = refreshTokenStore;
        _audit = audit;
        _options = options.Value;
    }

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;

        var email = NexaFlow.Domain.ValueObjects.Email.Create(request.Email);
        var normalizedEmail = email.Normalized;

        var user = await _db.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Section 8: do not reveal whether the email exists.
            // Audit LoginFailed — actor is unknown (no user resolved). The audit row
            // records the attempted login (no password, no payload). Persist BEFORE
            // throwing so the failure is recorded even though the request fails.
            await _audit.RecordAsync(
                action: AuditAction.LoginFailed,
                entity: "User",
                entityId: null,
                oldValues: null,
                newValues: null,
                actorUserIdOverride: null,
                organizationIdOverride: null,
                cancellationToken: cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new DomainException(InvalidCredentialsMessage, "INVALID_CREDENTIALS");
        }

        // Account lockout — checked before password verification to avoid the password
        // hash being computed on a locked account (minor DoS mitigation, also clearer UX).
        if (user.IsLockedOut)
        {
            throw new DomainException(
                "Account is locked. Try again later.",
                "ACCOUNT_LOCKED");
        }

        // Email verification gate (section 8 — required).
        if (!user.EmailVerified)
        {
            throw new DomainException(
                "Email is not verified. Please verify your email before logging in.",
                "EMAIL_NOT_VERIFIED");
        }

        // Verify the password. Constant-time comparison inside BCrypt.
        var passwordValid = _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (!passwordValid)
        {
            user.RegisterFailedLogin(
                maxAttempts: _options.MaxFailedLoginAttempts,
                lockoutDuration: _options.LockoutDuration,
                atUtc: now);

            // Audit LoginFailed — actor IS the user (we know who they are, even though
            // the password was wrong). Persist the failed-login counter AND the audit row
            // in the same transaction.
            await _audit.RecordAsync(
                action: AuditAction.LoginFailed,
                entity: "User",
                entityId: user.Id,
                oldValues: null,
                newValues: null,
                actorUserIdOverride: user.Id,
                organizationIdOverride: null,
                cancellationToken: cancellationToken);

            // SaveChanges persists the failed-login counter increment + audit row together.
            await _db.SaveChangesAsync(cancellationToken);

            throw new DomainException(InvalidCredentialsMessage, "INVALID_CREDENTIALS");
        }

        // Success — reset failure counter.
        user.RegisterSuccessfulLogin(now);

        // Issue a new refresh token family (login starts a new family).
        var refreshExpiry = now + _options.RefreshTokenLifetime;
        var refreshToken = await _refreshTokenStore.IssueNewAsync(
            userId: user.Id,
            expiresAtUtc: refreshExpiry,
            atUtc: now,
            createdFromIp: request.IpAddress,
            createdByUserAgent: null,
            cancellationToken);

        // Audit LoginSucceeded — same transaction as the user-state mutation + refresh token.
        await _audit.RecordAsync(
            action: AuditAction.LoginSucceeded,
            entity: "User",
            entityId: user.Id,
            oldValues: null,
            newValues: null,
            actorUserIdOverride: user.Id,
            organizationIdOverride: null,
            cancellationToken: cancellationToken);

        // Commit before issuing the access token.
        await _db.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user, now);
        var accessExpiry = now + _options.AccessTokenLifetime;

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken.PlaintextToken,
            AccessTokenExpiresAtUtc: accessExpiry,
            RefreshTokenExpiresAtUtc: refreshExpiry,
            User: new AuthUserDto(
                Id: user.Id,
                Email: user.Email.Value,
                DisplayName: user.DisplayName,
                EmailVerified: user.EmailVerified));
    }
}
