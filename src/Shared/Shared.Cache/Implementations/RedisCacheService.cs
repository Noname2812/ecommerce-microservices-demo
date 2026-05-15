using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using StackExchange.Redis;
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

    public RedisCacheService(IConnectionMultiplexer mux, IOptions<CacheOptions> options)
    {
        _mux = mux;
        _options = options.Value;
    }

    private IDatabase Db => _mux.GetDatabase();
    private string Prefix(string key) => $"{_options.InstanceName}:{key}";

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(Prefix(key));
        if (!value.HasValue) return default;
        return JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await Db.StringSetAsync(Prefix(key), json, expiry ?? _options.DefaultExpiry);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        Db.KeyDeleteAsync(Prefix(key));

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Db.KeyExistsAsync(Prefix(key));

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var value = await factory();
        await SetAsync(key, value, expiry, ct);
        return value;
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken ct = default)
    {
        var prefixedPattern = Prefix(pattern);
        // Use SCAN on each server — cluster-safe (KEYS is blocked on cluster nodes)
        foreach (var server in _mux.GetServers())
        {
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(pattern: prefixedPattern))
            {
                await Db.KeyDeleteAsync(key);
            }
        }
    }

    public Task<RedisResult> EvalAsync(string script, RedisKey[] keys, RedisValue[]? args = null, CancellationToken ct = default) =>
        Db.ScriptEvaluateAsync(script, keys, args);

    public Task<RedisResult> EvalAsync(LuaScript preparedScript, object? parameters = null, CancellationToken ct = default) =>
        preparedScript.EvaluateAsync(Db, parameters);
}
