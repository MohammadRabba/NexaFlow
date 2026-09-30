using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using StackExchange.Redis;

namespace NexaFlow.Infrastructure.Caching;

public sealed class RedisRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly IConnectionMultiplexer? _redis;
    private readonly RedisOptions _redisOptions;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<RedisRateLimiter> _logger;
    private volatile bool _redisAvailable = true;

    public RedisRateLimiter(
        IConnectionMultiplexer? redis,
        IOptions<RedisOptions> redisOptions,
        IOptions<CacheOptions> cacheOptions,
        ILogger<RedisRateLimiter> logger)
    {
        _redis = redis;
        _redisOptions = redisOptions.Value;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(string identity)
    {
        if (_redis is null || !_redisAvailable) return true;

        try
        {
            var db = _redis.GetDatabase();
            var key = $"{_cacheOptions.KeyPrefix}:rl:{identity}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60}";
            var count = await db.StringIncrementAsync(key);

            if (count == 1)
                await db.KeyExpireAsync(key, Window);

            _redisAvailable = true;
            return count <= _redisOptions.RateLimitPermitPerMinute;
        }
        catch (Exception ex)
        {
            _redisAvailable = false;
            _logger.LogWarning(ex, "Redis rate limit check failed — allowing request (fail-open).");
            return true;
        }
    }

    public static string GetIdentity(HttpContext context)
    {
        var userId = context.User?.FindFirst("sub")?.Value
            ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is not null) return $"user:{userId}";

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return $"ip:{ip}";
    }
}
