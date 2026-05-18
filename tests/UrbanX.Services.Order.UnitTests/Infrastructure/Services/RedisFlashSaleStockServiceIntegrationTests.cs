using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using StackExchange.Redis;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

[Trait("Category", "Integration")]
public sealed class RedisFlashSaleStockServiceIntegrationTests : IAsyncLifetime
{
    private const string InstanceName = "flashsale-it";
    private const string RedisConnection = "localhost:6379";

    private IConnectionMultiplexer? _mux;
    private IDatabase? _db;
    private RedisFlashSaleStockService? _sut;
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
            _sut = new RedisFlashSaleStockService(
                cache,
                Options.Create(new CacheOptions { InstanceName = InstanceName }),
                NullLogger<RedisFlashSaleStockService>.Instance);
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
        Skip.IfNot(_redisAvailable, "Redis is not available at localhost:6379");

    private string StockKey(Guid campaignId) =>
        $"{InstanceName}:flashsale:{campaignId:D}:stock";

    [Fact]
    public async Task TryReserve_NoStock_ReturnsSoldOut()
    {
        RequireRedis();
        var campaignId = Guid.NewGuid();
        await _db!.KeyDeleteAsync(StockKey(campaignId));

        var result = await _sut!.TryReserveAsync(campaignId, 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.FlashSaleSoldOut", result.Error.Code);
    }

    [Fact]
    public async Task TryReserve_QuantityExceedsRemaining_ReturnsSoldOut()
    {
        RequireRedis();
        var campaignId = Guid.NewGuid();
        var key = StockKey(campaignId);
        await _db!.StringSetAsync(key, 2);

        var result = await _sut!.TryReserveAsync(campaignId, 3, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(2, (int)await _db.StringGetAsync(key));
    }

    [Fact]
    public async Task ConcurrentReserve_StockFive_ExactlyFiveSucceed()
    {
        RequireRedis();
        var campaignId = Guid.NewGuid();
        var key = StockKey(campaignId);
        await _db!.StringSetAsync(key, 5);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _sut!.TryReserveAsync(campaignId, 1, CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var successes = results.Count(r => r.IsSuccess);
        var failures = results.Count(r => r.IsFailure);

        Assert.Equal(5, successes);
        Assert.Equal(5, failures);
        Assert.Equal(0, (int)await _db.StringGetAsync(key));
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
