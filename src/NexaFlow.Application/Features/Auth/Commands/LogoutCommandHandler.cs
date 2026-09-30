using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Domain.Entities;

namespace NexaFlow.Application.Features.Auth.Commands;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenStore refreshTokenStore,
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenStore = refreshTokenStore;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;

        // Idempotent: if the token is already revoked or doesn't exist, returns false
        // — we don't reveal that to the caller (the spec says logout is always 204).
        await _refreshTokenStore.RevokeAsync(
            presentedTokenPlaintext: request.RefreshToken,
            reason: "USER_LOGOUT",
            atUtc: now,
            cancellationToken);

        // Audit Logout. The actor is the authenticated user (logout requires authentication).
        // If somehow the user is unauthenticated, we still process the logout (token revocation
        // is safe-by-itself) but skip the audit row — there is no actor to record.
        if (_currentUser.IsAuthenticated && _currentUser.UserId is { } userId)
        {
            await _audit.RecordAsync(
                action: AuditAction.Logout,
                entity: "User",
                entityId: userId,
                actorUserIdOverride: userId,
                organizationIdOverride: null,
                cancellationToken: cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Refresh token revocation request processed at {AtUtc}.", now);
        return Unit.Value;
    }
}
