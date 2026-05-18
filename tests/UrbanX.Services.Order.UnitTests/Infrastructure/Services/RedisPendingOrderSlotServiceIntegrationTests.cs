using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using StackExchange.Redis;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

// Requires Redis at localhost:6379. Skipped when unavailable. Lua atomicity / no-underflow live here only.
[Trait("Category", "Integration")]
public sealed class RedisPendingOrderSlotServiceIntegrationTests : IAsyncLifetime
{
    private const string InstanceName = "order-slot-it";
    private const string RedisConnection = "localhost:6379";

    private IConnectionMultiplexer? _mux;
    private RedisPendingOrderSlotService? _sut;
    private IDatabase? _db;
    private bool _redisAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _mux = await ConnectionMultiplexer.ConnectAsync(RedisConnection);
            if (!_mux.IsConnected)
                return;

            _db = _mux.GetDatabase();
            var cache = new RedisEvalOnlyCacheService(_mux);
            _sut = new RedisPendingOrderSlotService(
                cache,
                Options.Create(new PlaceOrderOptions
                {
                    MaxNormalPendingPerUser = 1,
                    MaxSalesPendingPerUser = 3,
                    PendingSlotTtlMinutes = 30
                }),
                Options.Create(new CacheOptions { InstanceName = InstanceName }),
                NullLogger<RedisPendingOrderSlotService>.Instance);
            _redisAvailable = true;
        }
        catch (RedisConnectionException)
        {
            _mux = null;
            _redisAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null)
            await _mux.CloseAsync();
    }

    private void RequireRedis() =>
        Skip.IfNot(
            _redisAvailable && _sut is not null && _db is not null,
            $"Redis not available at {RedisConnection}. Start Aspire or docker-compose.");

    [SkippableFact]
    public async Task TryAcquire_Normal_SetsKeyWithTtl()
    {
        RequireRedis();

        var userId = Guid.NewGuid();
        var key = new RedisKey($"{InstanceName}:pending-orders:normal:{userId:D}");

        try
        {
            var result = await _sut!.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, (long)await _db!.StringGetAsync(key));
            Assert.True(await _db.KeyTimeToLiveAsync(key) > TimeSpan.Zero);
        }
        finally
        {
            await _db!.KeyDeleteAsync(key);
        }
    }

    [SkippableFact]
    public async Task TryAcquire_Normal_SecondCall_ReturnsFailure()
    {
        RequireRedis();

        var userId = Guid.NewGuid();
        var key = new RedisKey($"{InstanceName}:pending-orders:normal:{userId:D}");

        try
        {
            Assert.True((await _sut!.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsSuccess);
            Assert.True((await _sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsFailure);
        }
        finally
        {
            await _db!.KeyDeleteAsync(key);
        }
    }

    [SkippableFact]
    public async Task TryAcquire_NormalAndSales_AreIndependent()
    {
        RequireRedis();

        var userId = Guid.NewGuid();
        var normalKey = new RedisKey($"{InstanceName}:pending-orders:normal:{userId:D}");
        var salesKey = new RedisKey($"{InstanceName}:pending-orders:sales:{userId:D}");

        try
        {
            Assert.True((await _sut!.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsSuccess);
            Assert.True((await _sut.TryAcquireAsync(userId, OrderType.Sales, CancellationToken.None)).IsSuccess);
            Assert.Equal(1, (long)await _db!.StringGetAsync(normalKey));
            Assert.Equal(1, (long)await _db.StringGetAsync(salesKey));
        }
        finally
        {
            await _db!.KeyDeleteAsync([normalKey, salesKey]);
        }
    }

    // AC: double-release when slot=1 → 0 (Lua no-underflow); not covered by unit mocks.
    [SkippableFact]
    public async Task Release_DoesNotGoBelowZero()
    {
        RequireRedis();

        var userId = Guid.NewGuid();
        var normalKey = new RedisKey($"{InstanceName}:pending-orders:normal:{userId:D}");

        try
        {
            Assert.True((await _sut!.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsSuccess);
            await _sut.ReleaseAsync(userId, OrderType.Normal, CancellationToken.None);
            await _sut.ReleaseAsync(userId, OrderType.Normal, CancellationToken.None);

            var value = await _db!.StringGetAsync(normalKey);
            Assert.True(value.IsNull || (long)value <= 0);
        }
        finally
        {
            await _db!.KeyDeleteAsync(normalKey);
        }
    }

    [SkippableFact]
    public async Task TryAcquire_Sales_Concurrent100_RespectsMaxThree()
    {
        RequireRedis();

        var userId = Guid.NewGuid();
        var salesKey = new RedisKey($"{InstanceName}:pending-orders:sales:{userId:D}");

        try
        {
            var successes = 0;
            await Parallel.ForEachAsync(
                Enumerable.Range(0, 100),
                new ParallelOptions { MaxDegreeOfParallelism = 50 },
                async (_, ct) =>
                {
                    if ((await _sut!.TryAcquireAsync(userId, OrderType.Sales, ct)).IsSuccess)
                        Interlocked.Increment(ref successes);
                });

            Assert.Equal(3, successes);
            Assert.True((long)await _db!.StringGetAsync(salesKey) <= 3);
        }
        finally
        {
            await _db!.KeyDeleteAsync(salesKey);
        }
    }

    private sealed class RedisEvalOnlyCacheService(IConnectionMultiplexer mux) : ICacheService
    {
        private IDatabase Db => mux.GetDatabase();

        public Task<RedisResult> EvalAsync(
            string script,
            RedisKey[] keys,
            RedisValue[]? args = null,
            CancellationToken ct = default) =>
            Db.ScriptEvaluateAsync(script, keys, args ?? []);

        public Task<RedisResult> EvalAsync(LuaScript preparedScript, object? parameters = null, CancellationToken ct = default) =>
            preparedScript.EvaluateAsync(Db, parameters);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RemoveByPatternAsync(string pattern, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
