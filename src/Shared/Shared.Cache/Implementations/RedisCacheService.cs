using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Cache.Resilience;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Shared.Cache.Implementations;

internal sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new ResultJsonConverterFactory(), new PageResultJsonConverterFactory() }
    };

    private readonly IConnectionMultiplexer _mux;
    private readonly CacheOptions _options;
    private readonly RedisCircuitBreaker _circuit;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<RedisCacheService> _logger;

    // SingleFlight per-key registry — coalesces concurrent misses inside one process.
    // Stored as Task<object?> because the dictionary is non-generic.
    private readonly ConcurrentDictionary<string, Task<object?>> _flights = new();

    public RedisCacheService(
        IConnectionMultiplexer mux,
        IOptions<CacheOptions> options,
        RedisCircuitBreaker circuit,
        IDistributedLockService lockService,
        ILogger<RedisCacheService> logger)
    {
        _mux = mux;
        _options = options.Value;
        _circuit = circuit;
        _lockService = lockService;
        _logger = logger;
    }

    private IDatabase Db => _mux.GetDatabase();
    private string Prefix(string key) => $"{_options.InstanceName}:{key}";

    // ── Basic ops (silent fail-open on Redis errors) ─────────────────────────

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_circuit.ShouldSkipRedis()) return default;

        try
        {
            var value = await Db.StringGetAsync(Prefix(key));
            _circuit.RecordSuccess();
            if (!value.HasValue) return default;
            return JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis GET failed for '{Key}'. Falling back to miss.", key);
            _circuit.RecordFailure();
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        if (_circuit.ShouldSkipRedis()) return;

        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await Db.StringSetAsync(Prefix(key), json, expiry ?? _options.DefaultExpiry);
            _circuit.RecordSuccess();
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis SET failed for '{Key}'. Cache write skipped.", key);
            _circuit.RecordFailure();
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        if (_circuit.ShouldSkipRedis()) return;

        try
        {
            await Db.KeyDeleteAsync(Prefix(key));
            _circuit.RecordSuccess();
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis DEL failed for '{Key}'.", key);
            _circuit.RecordFailure();
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (_circuit.ShouldSkipRedis()) return false;

        try
        {
            var exists = await Db.KeyExistsAsync(Prefix(key));
            _circuit.RecordSuccess();
            return exists;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis EXISTS failed for '{Key}'.", key);
            _circuit.RecordFailure();
            return false;
        }
    }

    public async Task<IReadOnlyList<T?>> GetManyAsync<T>(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0) return Array.Empty<T?>();

        var results = new T?[keys.Count];
        if (_circuit.ShouldSkipRedis()) return results;

        try
        {
            var redisKeys = new RedisKey[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                redisKeys[i] = Prefix(keys[i]);

            var values = await Db.StringGetAsync(redisKeys);
            _circuit.RecordSuccess();

            for (var i = 0; i < values.Length; i++)
            {
                if (values[i].HasValue)
                    results[i] = JsonSerializer.Deserialize<T>((string)values[i]!, JsonOptions);
            }
            return results;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis MGET failed for {Count} keys. Falling back to all-miss.", keys.Count);
            _circuit.RecordFailure();
            return results;
        }
    }

    public async Task SetManyAsync<T>(IReadOnlyDictionary<string, T> items, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        if (items.Count == 0) return;
        if (_circuit.ShouldSkipRedis()) return;

        try
        {
            var ttl = expiry ?? _options.DefaultExpiry;
            // CreateBatch pipelines all commands in a single round-trip; each SET carries its own TTL
            // (MSET doesn't support per-key expiry).
            var batch = Db.CreateBatch();
            var tasks = new List<Task>(items.Count);
            foreach (var (key, value) in items)
            {
                var json = JsonSerializer.Serialize(value, JsonOptions);
                tasks.Add(batch.StringSetAsync(Prefix(key), json, ttl));
            }
            batch.Execute();
            await Task.WhenAll(tasks);
            _circuit.RecordSuccess();
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis batch SET failed for {Count} keys. Cache writes skipped.", items.Count);
            _circuit.RecordFailure();
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken ct = default)
    {
        if (_circuit.ShouldSkipRedis()) return;

        try
        {
            var prefixedPattern = Prefix(pattern);
            foreach (var server in _mux.GetServers())
            {
                if (!server.IsConnected || server.IsReplica) continue;
                await foreach (var key in server.KeysAsync(pattern: prefixedPattern).WithCancellation(ct))
                {
                    await Db.KeyDeleteAsync(key);
                }
            }
            _circuit.RecordSuccess();
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _logger.LogWarning(ex, "[Cache] Redis SCAN/DEL failed for pattern '{Pattern}'.", pattern);
            _circuit.RecordFailure();
        }
    }

    // ── Lua eval — throws (atomicity contract) ───────────────────────────────

    public async Task<RedisResult> EvalAsync(string script, RedisKey[] keys, RedisValue[]? args = null, CancellationToken ct = default)
    {
        try
        {
            var result = await Db.ScriptEvaluateAsync(script, keys, args);
            _circuit.RecordSuccess();
            return result;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _circuit.RecordFailure();
            throw;
        }
    }

    public async Task<RedisResult> EvalAsync(LuaScript preparedScript, object? parameters = null, CancellationToken ct = default)
    {
        try
        {
            var result = await preparedScript.EvaluateAsync(Db, parameters);
            _circuit.RecordSuccess();
            return result;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            _circuit.RecordFailure();
            throw;
        }
    }

    // ── Legacy GetOrSetAsync — forwards to stampede-safe overload ────────────

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var options = new GetOrSetOptions<T> { Expiry = expiry, UseSingleFlight = true };
        var result = await GetOrSetAsync<T>(key, _ => factory(), options, ct);
        // Legacy overload returns non-nullable T; null can only happen if factory itself returns null.
        return result!;
    }

    // ── New stampede-safe GetOrSetAsync overload ─────────────────────────────

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        GetOrSetOptions<T> options,
        CancellationToken ct = default)
    {
        // 1. Fast path: cache hit.
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        // 2. Negative cache check (only when caller opted in).
        if (options.NegativeTtl is not null && await ExistsAsync(key, ct))
            return default;

        // 3. SingleFlight (in-process stampede protection).
        if (!options.UseSingleFlight)
            return await LoadAndCacheAsync(key, factory, options, ct);

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = _flights.GetOrAdd(key, tcs.Task);

        // Follower: await the leader's task with our own cancellation.
        if (!ReferenceEquals(inFlight, tcs.Task))
            return (T?)await inFlight.WaitAsync(ct);

        // Leader: run factory, broadcast result/exception to followers.
        try
        {
            var value = await LoadAndCacheAsync(key, factory, options, ct);
            tcs.SetResult(value);
            return value;
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _flights.TryRemove(new KeyValuePair<string, Task<object?>>(key, tcs.Task));
        }
    }

    private async Task<T?> LoadAndCacheAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        GetOrSetOptions<T> options,
        CancellationToken ct)
    {
        ILockHandle? lockHandle = null;

        // Cross-process stampede protection (optional).
        if (options.UseDistributedLock && !_circuit.ShouldSkipRedis())
        {
            try
            {
                lockHandle = await _lockService.AcquireAsync(
                    $"getorset:{key}", options.LockExpiry, options.LockWaitTimeout, ct);
                _circuit.RecordSuccess();
            }
            catch (Exception ex) when (IsRedisFailure(ex))
            {
                _logger.LogWarning(ex,
                    "[Cache] Distributed lock acquire failed for '{Key}' — proceeding without lock.", key);
                _circuit.RecordFailure();
            }
        }

        try
        {
            // Double-check L2 under lock — another process may have just populated it.
            if (lockHandle is not null)
            {
                var second = await GetAsync<T>(key, ct);
                if (second is not null) return second;
            }

            var value = await factory(ct);

            if (value is not null)
            {
                var ttl = options.ExpirySelector?.Invoke(value) ?? options.Expiry;
                await SetAsync(key, value, ttl, ct);
            }
            else if (options.NegativeTtl is { } negTtl)
            {
                // Store a JSON "null" so ExistsAsync sees the key and short-circuits future misses.
                await SetAsync<T?>(key, default, negTtl, ct);
            }

            return value;
        }
        finally
        {
            if (lockHandle is not null)
                await lockHandle.DisposeAsync();
        }
    }

    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisException or RedisTimeoutException or RedisConnectionException or TimeoutException;
}
