using System.Security.Claims;
using System.Security.Cryptography;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Infrastructure.Services;

/// <summary>
///     Reads the authenticated principal's claims from <see cref="ClaimsPrincipal" />
///     populated by the JWT bearer middleware. Populated by the Api host's HTTP
///     middleware via <c>AsyncLocal</c>; consumed in Domain / Application layers as
///     <c>ICurrentUserService</c> (no reference to <c>Microsoft.AspNetCore</c>).
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private static readonly AsyncLocal<CurrentPrincipal?> _principal = new();

    public Guid? UserId => _principal.Value?.UserId;
    public bool IsAuthenticated => _principal.Value?.IsAuthenticated ?? false;
    public string? IPAddress => _principal.Value?.IPAddress;
    public string? TraceId => _principal.Value?.TraceId;

    /// <summary>
    ///     Populate the ambient principal. Called by HTTP middleware in the Api host
    ///     (Phase 2+ — when JWT bearer authentication is wired). In Phase 1, this method
    ///     is called by the test fixture / background-worker host.
    /// </summary>
    internal static void SetPrincipal(
        Guid? userId,
        bool isAuthenticated,
        string? ipAddress,
        string? traceId)
    {
        _principal.Value = new CurrentPrincipal(userId, isAuthenticated, ipAddress, traceId);
    }

    private sealed record CurrentPrincipal(
        Guid? UserId,
        bool IsAuthenticated,
        string? IPAddress,
        string? TraceId);
}
