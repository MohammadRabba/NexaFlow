using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Events.Users;
using NexaFlow.Domain.Exceptions;
using NexaFlow.Domain.ValueObjects;

namespace NexaFlow.Application.Features.Auth.Commands;

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly AuthOptions _options;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IRefreshTokenStore refreshTokenStore,
        IOptions<AuthOptions> options,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _refreshTokenStore = refreshTokenStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;

        var email = NexaFlow.Domain.ValueObjects.Email.Create(request.Email);
        var normalizedEmail = email.Normalized;

        var user = await _db.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Section 8 — do not reveal. Generic 400 with INVALID_RESET_TOKEN.
            throw new DomainException("Reset token is invalid or expired.", "INVALID_RESET_TOKEN");
        }

        // Compute the hash of the presented token to compare against the stored hash.
        var presentedTokenHash = _tokenGenerator.HashPlaintext(request.Token);
        var consumed = user.ConsumePasswordResetToken(
            tokenHash: presentedTokenHash,
            newPasswordHash: _passwordHasher.Hash(request.NewPassword),
            atUtc: now);

        if (!consumed)
        {
            // Token mismatch, expired, or already used. Generic 400.
            throw new DomainException("Reset token is invalid or expired.", "INVALID_RESET_TOKEN");
        }

        // Raise a domain event — PasswordChangedEvent lets audit logging / outbox handle it.
        user.AddDomainEvent(new PasswordChangedEvent(
            UserId: user.Id,
            PasswordChangedAtUtc: user.PasswordChangedAtUtc!.Value,
            OccurredOnUtc: now));

        // Invalidate ALL refresh tokens for the user — single-use token consumption MUST
        // invalidate existing sessions (defense against stolen sessions post-reset).
        await _refreshTokenStore.RevokeAllForUserAsync(
            userId: user.Id,
            reason: "PASSWORD_RESET",
            atUtc: now,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Password reset completed for user {UserId} at {AtUtc}. All sessions revoked.",
            user.Id,
            now);
        return Unit.Value;
    }
}
