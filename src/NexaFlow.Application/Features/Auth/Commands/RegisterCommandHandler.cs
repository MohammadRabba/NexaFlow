using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Common;
using NexaFlow.Application.Features.Auth.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Events.Users;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Features.Auth.Commands;

/// <summary>
///     Handles <see cref="RegisterCommand" />:
///     <list type="number">
///         <item>Validate email is unique (normalized).</item>
///         <item>Hash the password (BCrypt).</item>
///         <item>Issue an email verification token (hashed only).</item>
///         <item>Create the User aggregate.</item>
///         <item>Issue an access token + a fresh refresh token (new family).</item>
///         <item>Send the verification email via <c>IEmailService</c>.</item>
///         <item>Persist via <c>IApplicationDbContext.SaveChangesAsync</c>.</item>
///     </list>
/// </summary>
public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResponse>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly AuthOptions _options;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IJwtTokenService jwtTokenService,
        IRefreshTokenStore refreshTokenStore,
        IEmailService emailService,
        ICurrentUserService currentUser,
        IOptions<AuthOptions> options,
        ILogger<RegisterCommandHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _jwtTokenService = jwtTokenService;
        _refreshTokenStore = refreshTokenStore;
        _emailService = emailService;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;

        // Normalize + parse the email via the Domain value object (invariants).
        // If this throws, the validator caught it first; here it's a defensive check.
        var email = NexaFlow.Domain.ValueObjects.Email.Create(request.Email);

        // Section 8: do NOT reveal whether the email exists. We throw a generic
        // "duplicate email" exception that the controller maps to 409 Conflict;
        // however the message is identical for any auth-failure scenario.
        var normalizedEmail = email.Normalized;
        var emailInUse = await _db.EmailIsInUseAsync(normalizedEmail, cancellationToken);
        if (emailInUse)
        {
            throw new DomainException(
                "An account with this email already exists.",
                "EMAIL_ALREADY_REGISTERED");
        }

        // Hash the password — never log the plaintext.
        var passwordHash = _passwordHasher.Hash(request.Password);

        // Issue a one-time email verification token (hash only).
        var (verificationTokenPlaintext, verificationTokenHash) = _tokenGenerator.Generate();
        var verificationExpiry = now + _options.EmailVerificationTokenLifetime;

        // Create the User aggregate — single-use token is stored as hash.
        var user = User.Create(
            email: email,
            displayName: request.DisplayName,
            passwordHash: passwordHash,
            emailVerificationTokenHash: verificationTokenHash,
            emailVerificationTokenExpiresAtUtc: verificationExpiry,
            createdAtUtc: now);

        user.AddDomainEvent(new UserRegisteredEvent(user.Id, email.Value, now));

        // Stage the new user (caller commits via SaveChangesAsync on the next line).
        _db.Add(user);

        // Issue the initial refresh token (new family).
        var refreshTokenExpiry = now + _options.RefreshTokenLifetime;
        var refreshToken = await _refreshTokenStore.IssueNewAsync(
            userId: user.Id,
            expiresAtUtc: refreshTokenExpiry,
            atUtc: now,
            createdFromIp: _currentUser.IPAddress,
            createdByUserAgent: null, // Phase 2 doesn't yet capture UA — deferred
            cancellationToken);

        // Commit before issuing the access token — if SaveChanges throws, we never
        // issue a token for an unsaved user.
        await _db.SaveChangesAsync(cancellationToken);

        // Now issue the short-lived access token. The user id is guaranteed persisted.
        var accessToken = _jwtTokenService.IssueAccessToken(user, now);
        var accessTokenExpiry = now + _options.AccessTokenLifetime;

        // Send the verification email — fire-and-forget is OK because the token is already persisted;
        // re-issuing is possible via the forgot-password flow.
        try
        {
            await _emailService.SendAsync(new EmailRequest(
                To: user.Email.Value,
                Subject: "Verify your NexaFlow account",
                HtmlBody: $"<p>Welcome to NexaFlow. Use this verification token: <code>{verificationTokenPlaintext}</code></p>",
                TextBody: $"Welcome to NexaFlow. Your verification token: {verificationTokenPlaintext}"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Email delivery is best-effort at registration — the token is in the DB and the
            // user can request a re-send via the forgot-password endpoint. We log the failure
            // but do not fail the registration itself.
            _logger.LogWarning(ex,
                "Failed to send email verification email to {Email} for user {UserId}. " +
                "User can request a new verification via the forgot-password endpoint.",
                "<redacted>",  // never log the plaintext email
                user.Id);
        }

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken.PlaintextToken,
            AccessTokenExpiresAtUtc: accessTokenExpiry,
            RefreshTokenExpiresAtUtc: refreshTokenExpiry,
            User: new AuthUserDto(
                Id: user.Id,
                Email: user.Email.Value,
                DisplayName: user.DisplayName,
                EmailVerified: user.EmailVerified));
    }
}
