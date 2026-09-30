using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaFlow.Application.Abstractions;
using StackExchange.Redis;

namespace NexaFlow.Infrastructure.Caching;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public int RateLimitPermitPerMinute { get; set; } = 100;
}

/// <summary>
///     Redis-backed cache service. Fails open: if Redis is unavailable, returns null
/// (cache miss) and logs a warning — the caller falls through to PostgreSQL.
/// Never throws for Redis connection failures.
/// </summary>
public sealed class RedisCacheService : ICacheService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer? _redis;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<RedisCacheService> _logger;
    private volatile bool _redisAvailable = true;

    public RedisCacheService(
        IConnectionMultiplexer? redis,
        IOptions<CacheOptions> cacheOptions,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_redis is null || !_redisAvailable) return default;

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = PrefixKey(key);
            var value = await db.StringGetAsync(prefixedKey);

            if (value.IsNullOrEmpty) return default;

            _redisAvailable = true;
            return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            _redisAvailable = false;
            _logger.LogWarning(ex, "Redis GET failed for {Key} — falling through to database.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_redis is null || !_redisAvailable) return;

        try
        {
            var db = _redis.GetDatabase();
            var prefixedKey = PrefixKey(key);
            var serialized = JsonSerializer.Serialize(value, JsonOptions);
            await db.StringSetAsync(prefixedKey, serialized, expiration);
            _redisAvailable = true;
        }
        catch (Exception ex)
        {
            _redisAvailable = false;
            _logger.LogWarning(ex, "Redis SET failed for {Key} — cache not populated.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_redis is null || !_redisAvailable) return;

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(PrefixKey(key));
            _redisAvailable = true;
        }
        catch (Exception ex)
        {
            _redisAvailable = false;
            _logger.LogWarning(ex, "Redis REMOVE failed for {Key} — stale entry may persist.", key);
        }
    }

    private string PrefixKey(string key) => $"{_cacheOptions.KeyPrefix}:{key}";

    public async ValueTask DisposeAsync()
    {
        if (_redis is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
