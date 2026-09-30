# ADR-010: Redis Caching and Distributed Rate Limiting

- Status: Accepted
- Date: 2026-09-30
- Phase: 7 — Redis

## Context

Section 23 of the master specification requires Redis for:
- Caching `organization:{id}`, `project:{id}`, `user:{id}`
- Cache expiration and invalidation
- Distributed rate limiting

The existing in-memory rate limiter (Phase 1's `FixedWindowLimiter`) is per-instance
and doesn't share state across multiple API instances. Section 23: "Prefer distributed
rate limiting when multiple application instances are expected."

## Decision

### Cache-aside pattern with Redis

```
Read request → ICacheService.GetAsync(key)
    ├── hit → return cached (but still verify authorization via DB)
    └── miss → query PostgreSQL → ICacheService.SetAsync(key, value, ttl) → return
```

Key naming: `{prefix}:{type}:{id}` (e.g., `nexaflow:organization:{id}`)
TTL: configurable per resource type (default 5 minutes).

### Fail-open on Redis unavailability

When Redis is unavailable:
- **Caching**: returns null (cache miss) → handler queries PostgreSQL directly
- **Rate limiting**: allows the request (fail-open) — better to allow extra traffic
  than to block all users when an optional cache is down

The app does NOT crash on Redis connection failure. `ConnectionMultiplexer` is
attempted at startup; if it fails, the app continues without caching.

### Distributed rate limiting via Redis INCR + EXPIRE

```
RedisRateLimiter.TryAcquireAsync(identity)
    → INCR nexaflow:rl:{identity}:{minute_bucket}
    → if count == 1: EXPIRE key 60s
    → return count <= limit
```

Identity: JWT `sub` claim for authenticated users; IP for anonymous. Server-derived,
never client-controlled.

### Cache invalidation on mutations

```
Mutation handler (Update/Delete) → SaveChanges → ICacheService.RemoveAsync(key)
```

Called after successful DB commit. If the remove fails (Redis unavailable), the
cached entry expires via TTL — stale data is bounded by the TTL window.

### Architecture boundaries

- `CacheOptions` (TTLs, prefix) lives in **Application.Abstractions** — handlers
  read TTLs without depending on Infrastructure
- `RedisOptions` (connection string, rate limit settings) lives in **Infrastructure**
- `RedisCacheService` implements `ICacheService` (Application abstraction) in Infrastructure
- `RedisRateLimiter` is Infrastructure-only (used by API middleware)
- Domain has zero Redis knowledge

## Alternatives Considered

- **In-memory caching only**: Rejected. Doesn't share state across instances. Each
  instance has its own cache, leading to inconsistent results after mutations.
- **PostgreSQL as cache**: Rejected. Defeats the purpose of offloading read pressure
  from the primary database.
- **Write-through caching**: Rejected for Phase 7. Adds complexity (dual writes);
  cache-aside is simpler and sufficient for the current read/write ratio.
- **Event-based cache invalidation via RabbitMQ**: Rejected. Direct invalidation
  in the mutation handler is simpler and doesn't require the consumer to be running.
  RabbitMQ is for cross-service async events, not for cache invalidation.

## Consequences

- **Positive**: Read queries for Organization/Project are served from Redis when
  available, reducing PostgreSQL load. Rate limits are shared across instances.
- **Positive**: Redis failure is non-fatal — the app continues to function, just
  without caching. Rate limiting fails open (allows traffic).
- **Negative**: Cache invalidation is best-effort. If Redis is down when a mutation
  occurs, the old cached value remains until TTL expiry. Acceptable: 5-minute TTL
  bounds staleness.
- **Negative**: Authorization is still verified on every request (even cache hits)
  by loading the entity from PostgreSQL. This means cache hits save the DTO
  serialization but still hit the DB for membership checks. This is deliberate:
  section 23 mandates "Authorization must remain correct even if cache state is stale."
