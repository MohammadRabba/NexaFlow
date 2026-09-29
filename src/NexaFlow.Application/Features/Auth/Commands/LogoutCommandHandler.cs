using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Application.Features.Auth.Commands;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenStore refreshTokenStore,
        IApplicationDbContext db,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenStore = refreshTokenStore;
        _db = db;
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

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Refresh token revocation request processed at {AtUtc}.", now);
        return Unit.Value;
    }
}
