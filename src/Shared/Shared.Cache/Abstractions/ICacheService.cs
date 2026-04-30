using StackExchange.Redis;

namespace Shared.Cache.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Cache-aside: get from cache or invoke factory and store result.</summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default);

    /// <summary>Remove all keys matching a glob pattern. Uses SCAN — safe for Redis Cluster.</summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken ct = default);

    /// <summary>Execute a raw Lua script. Keys and args are forwarded as-is to Redis.</summary>
    Task<RedisResult> EvalAsync(string script, RedisKey[] keys, RedisValue[]? args = null, CancellationToken ct = default);

    /// <summary>Execute a pre-compiled Lua script (use LuaScript.Prepare for reuse).</summary>
    Task<RedisResult> EvalAsync(LuaScript preparedScript, object? parameters = null, CancellationToken ct = default);
}
