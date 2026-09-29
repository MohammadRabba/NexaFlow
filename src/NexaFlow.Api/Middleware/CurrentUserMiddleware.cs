using System.Diagnostics;
using System.Security.Claims;
using NexaFlow.Infrastructure.Services;

namespace NexaFlow.Api.Middleware;

/// <summary>
///     Populates <see cref="CurrentUserService" /> with the ambient authenticated
///     principal. Phase 2 will add JWT bearer authentication — for Phase 1, the
///     principal is anonymous but the wiring is in place.
/// </summary>
internal sealed class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        CurrentUserService.SetPrincipal(
            userId: Guid.TryParse(userId, out var uid) ? uid : null,
            isAuthenticated: context.User?.Identity?.IsAuthenticated ?? false,
            ipAddress: ip,
            traceId: traceId);

        await _next(context);
    }
}
