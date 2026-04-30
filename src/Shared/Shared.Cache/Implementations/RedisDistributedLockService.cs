using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using StackExchange.Redis;

namespace Shared.Cache.Implementations;

internal sealed class RedisDistributedLockService : IDistributedLockService
{
    // Lua script: release lock only if token matches (atomic check-and-delete)
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end");

    private readonly IConnectionMultiplexer _mux;
    private readonly CacheOptions _options;

    public RedisDistributedLockService(IConnectionMultiplexer mux, IOptions<CacheOptions> options)
    {
        _mux = mux;
        _options = options.Value;
    }

    private IDatabase Db => _mux.GetDatabase();
    private string LockKey(string resource) => $"{_options.InstanceName}:lock:{resource}";

    public async Task<ILockHandle?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default)
    {
        var key = LockKey(resource);
        var token = Guid.NewGuid().ToString("N");

        // SET key token NX PX ttl  — atomic, cluster-safe
        var acquired = await Db.StringSetAsync(key, token, expiry, When.NotExists);
        if (!acquired) return null;

        return new RedisLockHandle(Db, key, token, resource);
    }

    public async Task<ILockHandle?> AcquireAsync(
        string resource, TimeSpan expiry, TimeSpan waitTimeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + waitTimeout;
        var delay = TimeSpan.FromMilliseconds(50);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var handle = await TryAcquireAsync(resource, expiry, ct);
            if (handle is not null) return handle;
            await Task.Delay(delay, ct);
        }

        return null;
    }

    private sealed class RedisLockHandle : ILockHandle
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private bool _released;

        public bool IsAcquired => true;
        public string Resource { get; }

        public RedisLockHandle(IDatabase db, string key, string token, string resource)
        {
            _db = db;
            _key = key;
            _token = token;
            Resource = resource;
        }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            await ReleaseScript.EvaluateAsync(_db, new { KEYS = new RedisKey[] { _key }, ARGV = new RedisValue[] { _token } });
        }
    }
}
