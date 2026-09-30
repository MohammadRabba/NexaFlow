using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Events.Users;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Auth.Commands;

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private const string InvalidCurrentPasswordMessage = "Current password is incorrect.";

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly ICacheService _cache;
    private readonly AuthOptions _options;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IRefreshTokenStore refreshTokenStore,
        ICacheService cache,
        IOptions<AuthOptions> options,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _refreshTokenStore = refreshTokenStore;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;

        var user = await _db.FindUserByIdAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            // Don't reveal whether the user exists.
            throw new DomainException(InvalidCurrentPasswordMessage, "INVALID_CURRENT_PASSWORD");
        }

        // Verify current password (constant-time inside BCrypt).
        var currentPasswordValid = _passwordHasher.Verify(request.CurrentPassword, user.PasswordHash);
        if (!currentPasswordValid)
        {
            throw new DomainException(InvalidCurrentPasswordMessage, "INVALID_CURRENT_PASSWORD");
        }

        // Apply the new password (domain method also stamps PasswordChangedAtUtc).
        var newHash = _passwordHasher.Hash(request.NewPassword);
        user.ChangePassword(newHash, now);

        // Raise the event — audit log + outbox (Phase 6).
        user.AddDomainEvent(new PasswordChangedEvent(Guid.NewGuid(),
            UserId: user.Id,
            PasswordChangedAtUtc: user.PasswordChangedAtUtc!.Value,
            OccurredOnUtc: now));

        // Revoke refresh tokens issued BEFORE the password change. Tokens issued AFTER
        // (which would be impossible here since this is the change operation) survive.
        // Wait — actually we revoke ALL refresh tokens, including the one that may be
        // currently in use for this session (since the user is authenticated via access
        // token, not refresh token). The user must re-authenticate.
        await _refreshTokenStore.RevokeAllForUserAsync(
            userId: user.Id,
            reason: "PASSWORD_CHANGED",
            atUtc: now,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Phase 7: Invalidate user cache — password/security state changed.
        await _cache.RemoveAsync($"user:{user.Id}", cancellationToken);

        _logger.LogInformation(
            "Password changed for user {UserId} at {AtUtc}. All refresh tokens revoked.",
            user.Id,
            now);
        return Unit.Value;
    }
}
