namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Cache TTL configuration. Lives in Application so query handlers can read
///     the TTLs without depending on Infrastructure (where the Redis implementation lives).
/// </summary>
public sealed class CacheOptions
{
    public string KeyPrefix { get; set; } = "nexaflow";
    public TimeSpan OrganizationCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ProjectCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan UserCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}
