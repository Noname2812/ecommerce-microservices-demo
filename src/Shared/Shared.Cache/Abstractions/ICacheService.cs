using StackExchange.Redis;

namespace Shared.Cache.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Bulk GET via Redis MGET — single round-trip, returns one entry per key in input order.
    /// Missing keys return <c>default(T)</c>. Falls back to all-misses if the circuit is open.
    /// </summary>
    Task<IReadOnlyList<T?>> GetManyAsync<T>(IReadOnlyList<string> keys, CancellationToken ct = default);

    /// <summary>
    /// Bulk SET via pipelined StringSet (each entry shares <paramref name="expiry"/>). No-op when the
    /// circuit is open or the input is empty. Failures degrade silently — cache writes are best-effort.
    /// </summary>
    Task SetManyAsync<T>(IReadOnlyDictionary<string, T> items, TimeSpan? expiry = null, CancellationToken ct = default);

    /// <summary>Cache-aside: get from cache or invoke factory and store result.</summary>
    /// <remarks>
    /// Forwards to <see cref="GetOrSetAsync{T}(string, Func{CancellationToken, Task{T}}, GetOrSetOptions{T}, CancellationToken)"/>
    /// with <c>UseSingleFlight = true</c> by default — concurrent misses on the same key
    /// inside a single process are coalesced into one factory call.
    /// </remarks>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default);

    /// <summary>
    /// Stampede-safe cache-aside with optional dynamic TTL, distributed locking, and negative caching.
    /// </summary>
    /// <param name="key">Cache key (will be prefixed by <c>InstanceName</c>).</param>
    /// <param name="factory">Producer invoked when cache misses. Receives the caller's <see cref="CancellationToken"/>.</param>
    /// <param name="options">Resilience and TTL configuration. See <see cref="GetOrSetOptions{T}"/>.</param>
    /// <param name="ct">Caller cancellation token.</param>
    /// <returns>The cached or freshly produced value. May be <c>null</c> if factory returns <c>null</c>.</returns>
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        GetOrSetOptions<T> options,
        CancellationToken ct = default);

    /// <summary>Remove all keys matching a glob pattern. Uses SCAN — safe for Redis Cluster.</summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken ct = default);

    /// <summary>Execute a raw Lua script. Keys and args are forwarded as-is to Redis.</summary>
    /// <remarks>
    /// Does NOT swallow Redis exceptions — Lua atomicity is a contract callers rely on.
    /// Callers must handle <see cref="RedisException"/>/<see cref="RedisTimeoutException"/> explicitly.
    /// </remarks>
    Task<RedisResult> EvalAsync(string script, RedisKey[] keys, RedisValue[]? args = null, CancellationToken ct = default);

    /// <summary>Execute a pre-compiled Lua script (use LuaScript.Prepare for reuse).</summary>
    /// <remarks>Same throw-on-failure semantics as the raw-script overload.</remarks>
    Task<RedisResult> EvalAsync(LuaScript preparedScript, object? parameters = null, CancellationToken ct = default);
}
