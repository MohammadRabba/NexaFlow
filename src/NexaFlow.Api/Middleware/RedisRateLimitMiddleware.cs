using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NexaFlow.Infrastructure.Caching;

namespace NexaFlow.Api.Middleware;

/// <summary>
///     Redis-backed distributed rate limiting middleware. Checks Redis before
/// allowing the request through. Fails open (allows request) if Redis is unavailable.
/// Uses server-derived identity (JWT user ID for authenticated, IP for anonymous).
/// </summary>
internal sealed class RedisRateLimitMiddleware
{
    private readonly RequestDelegate _next;

    public RedisRateLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        RedisRateLimiter limiter,
        ILogger<RedisRateLimitMiddleware> logger)
    {
        var identity = RedisRateLimiter.GetIdentity(context);
        var allowed = await limiter.TryAcquireAsync(identity);

        if (!allowed)
        {
            logger.LogInformation("Rate limit exceeded for {Identity}.", identity);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = "60";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://api.nexaflow.com/errors/rate_limited",
                title = "Too many requests",
                status = 429,
                detail = "Rate limit exceeded. Try again in 60 seconds.",
                errorCode = "RATE_LIMITED",
                traceId = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}
