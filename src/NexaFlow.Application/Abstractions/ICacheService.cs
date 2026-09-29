namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction for caching (section 23). <b>DEFERRED</b> — Phase 7 introduces
///     a Redis-backed implementation. The contract is here so handlers can take a
///     dependency in later phases without restructuring.
///     <para>
///         Section 23: "Authorization must remain correct even if cache state is stale."
///         Implementations MUST NOT cache authorization / tenant resolution data in a way
///         that can bypass security. Cache only read-side, idempotent projections.
///     </para>
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
