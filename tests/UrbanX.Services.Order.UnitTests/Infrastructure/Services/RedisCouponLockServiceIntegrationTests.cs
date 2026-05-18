using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

[Trait("Category", "Integration")]
public sealed class RedisCouponLockServiceIntegrationTests : IAsyncLifetime
{
    private const string InstanceName = "coupon-it";
    private const string RedisConnection = "localhost:6379";
    private const string CouponCode = "IT-SAVE10";

    private IConnectionMultiplexer? _mux;
    private IDatabase? _db;
    private RedisCouponLockService? _sut;
    private bool _redisAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _mux = await ConnectionMultiplexer.ConnectAsync(RedisConnection);
            if (!_mux.IsConnected)
                return;

            _db = _mux.GetDatabase();
            var placeOptions = new Mock<IOptionsMonitor<PlaceOrderOptions>>();
            placeOptions.Setup(x => x.CurrentValue).Returns(new PlaceOrderOptions { CouponLockTtlSeconds = 60 });

            _sut = new RedisCouponLockService(
                new RedisEvalOnlyCacheService(_mux),
                Options.Create(new CacheOptions { InstanceName = InstanceName }),
                placeOptions.Object,
                NullLogger<RedisCouponLockService>.Instance);
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

    [SkippableFact]
    public async Task Lock_Release_RestoresRemainingQuota()
    {
        RequireRedis();
        var userId = Guid.NewGuid();
        await SeedCouponAsync(userId, remaining: 5);

        var lockResult = await _sut!.TryLockAsync(CouponCode, userId, CancellationToken.None);
        Assert.True(lockResult.IsSuccess);
        Assert.Equal(50_000m, lockResult.Value!.DiscountAmount);

        Assert.Equal(4, (int)await _db!.StringGetAsync(RemainingKey()));
        Assert.True(await _db.SetContainsAsync(LockedUsersKey(), userId.ToString("D")));

        await _sut.ReleaseAsync(CouponCode, userId, CancellationToken.None);

        Assert.Equal(5, (int)await _db.StringGetAsync(RemainingKey()));
        Assert.False(await _db.SetContainsAsync(LockedUsersKey(), userId.ToString("D")));
    }

    [SkippableFact]
    public async Task Lock_ConfirmUse_SecondLockReturnsAlreadyUsed()
    {
        RequireRedis();
        var userId = Guid.NewGuid();
        await SeedCouponAsync(userId, remaining: 2);

        Assert.True((await _sut!.TryLockAsync(CouponCode, userId, CancellationToken.None)).IsSuccess);
        await _sut.ConfirmUseAsync(CouponCode, userId, CancellationToken.None);

        Assert.True(await _db!.SetContainsAsync(UsedUsersKey(), userId.ToString("D")));
        Assert.False(await _db.SetContainsAsync(LockedUsersKey(), userId.ToString("D")));

        var second = await _sut.TryLockAsync(CouponCode, userId, CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(OrderErrors.CouponAlreadyUsed.Code, second.Error.Code);
    }

    [SkippableFact]
    public async Task Lock_ConcurrentClaim_ReturnsCouponConcurrentClaim()
    {
        RequireRedis();
        var userId = Guid.NewGuid();
        await SeedCouponAsync(userId, remaining: 5);

        Assert.True((await _sut!.TryLockAsync(CouponCode, userId, CancellationToken.None)).IsSuccess);

        var second = await _sut.TryLockAsync(CouponCode, userId, CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(OrderErrors.CouponConcurrentClaim.Code, second.Error.Code);
    }

    private async Task SeedCouponAsync(Guid userId, int remaining)
    {
        var keys = KeyNames();
        await _db!.KeyDeleteAsync(keys);
        await _db.SetAddAsync(keys[0], userId.ToString("D"));
        await _db.StringSetAsync(keys[1], remaining);
        await _db.StringSetAsync(keys[4], "50000");
        await _db.StringSetAsync(keys[5], "FIXED");
    }

    private RedisKey[] KeyNames() =>
    [
        $"{InstanceName}:coupon:{CouponCode}:eligible-users",
        $"{InstanceName}:coupon:{CouponCode}:remaining",
        $"{InstanceName}:coupon:{CouponCode}:locked-users",
        $"{InstanceName}:coupon:{CouponCode}:used-users",
        $"{InstanceName}:coupon:{CouponCode}:meta-discount",
        $"{InstanceName}:coupon:{CouponCode}:meta-discount-type"
    ];

    private RedisKey RemainingKey() => $"{InstanceName}:coupon:{CouponCode}:remaining";
    private RedisKey LockedUsersKey() => $"{InstanceName}:coupon:{CouponCode}:locked-users";
    private RedisKey UsedUsersKey() => $"{InstanceName}:coupon:{CouponCode}:used-users";

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
