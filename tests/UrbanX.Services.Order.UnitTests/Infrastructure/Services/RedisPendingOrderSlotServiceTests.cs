using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

// Release no-underflow (double-release) is Lua script behavior — see integration tests only.
public sealed class RedisPendingOrderSlotServiceTests
{
    private const string InstanceName = "test-order";
    private readonly Mock<ICacheService> _cache = new();
    private readonly PlaceOrderOptions _options = new()
    {
        MaxNormalPendingPerUser = 1,
        MaxSalesPendingPerUser = 3,
        PendingSlotTtlMinutes = 30
    };

    private RedisPendingOrderSlotService CreateSut()
    {
        _cache
            .Setup(c => c.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.Is<RedisValue[]?>(a => a == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)0));

        return new RedisPendingOrderSlotService(
            _cache.Object,
            Options.Create(_options),
            Options.Create(new CacheOptions { InstanceName = InstanceName }),
            NullLogger<RedisPendingOrderSlotService>.Instance);
    }

    [Fact]
    public async Task TryAcquire_Normal_FirstCall_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        SetupAcquire($"{InstanceName}:pending-orders:normal:{userId:D}", 1);

        var result = await CreateSut().TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TryAcquire_Normal_SecondCall_WhenMaxIsOne_ReturnsTooManyPending()
    {
        var userId = Guid.NewGuid();
        var key = $"{InstanceName}:pending-orders:normal:{userId:D}";
        SetupAcquireSequence(key, 1, 0);

        var sut = CreateSut();
        Assert.True((await sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsSuccess);

        var second = await sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(OrderErrors.TooManyPendingOrders.Code, second.Error.Code);
    }

    [Fact]
    public async Task TryAcquire_Sales_ThreeCalls_SucceedThenFourthFails()
    {
        var userId = Guid.NewGuid();
        var key = $"{InstanceName}:pending-orders:sales:{userId:D}";
        SetupAcquireSequence(key, 1, 2, 3, 0);

        var sut = CreateSut();
        for (var i = 0; i < 3; i++)
            Assert.True((await sut.TryAcquireAsync(userId, OrderType.Sales, CancellationToken.None)).IsSuccess);

        var fourth = await sut.TryAcquireAsync(userId, OrderType.Sales, CancellationToken.None);
        Assert.True(fourth.IsFailure);
        Assert.Equal(OrderErrors.TooManyPendingOrders.Code, fourth.Error.Code);
    }

    [Fact]
    public async Task TryAcquire_NormalAndSales_UseSeparateCounters()
    {
        var userId = Guid.NewGuid();
        SetupAcquire($"{InstanceName}:pending-orders:normal:{userId:D}", 1);
        SetupAcquire($"{InstanceName}:pending-orders:sales:{userId:D}", 1);

        var sut = CreateSut();
        var normal = await sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None);
        var sales = await sut.TryAcquireAsync(userId, OrderType.Sales, CancellationToken.None);

        Assert.True(normal.IsSuccess);
        Assert.True(sales.IsSuccess);
    }

    [Fact]
    public async Task TryAcquire_Normal_PassesTtlAndMaxToRedis()
    {
        var userId = Guid.NewGuid();
        RedisValue[]? capturedArgs = null;

        _cache
            .Setup(c => c.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RedisKey[], RedisValue[], CancellationToken>((_, _, args, _) => capturedArgs = args)
            .ReturnsAsync(RedisResult.Create((RedisValue)1));

        await CreateSut().TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None);

        Assert.NotNull(capturedArgs);
        Assert.Equal(_options.PendingSlotTtlMinutes * 60, (int)capturedArgs![0]);
        Assert.Equal(_options.MaxNormalPendingPerUser, (int)capturedArgs[1]);
    }

    [Theory]
    [InlineData(OrderType.Normal, "normal")]
    [InlineData(OrderType.Sales, "sales")]
    public async Task Release_DecrementsOnlyMatchingOrderTypeKey(string orderType, string keySegment)
    {
        var userId = Guid.NewGuid();
        var expectedKey = $"{InstanceName}:pending-orders:{keySegment}:{userId:D}";
        string? releasedKey = null;

        var sut = CreateSut();
        _cache
            .Setup(c => c.EvalAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.Is<RedisValue[]?>(a => a == null),
                It.IsAny<CancellationToken>()))
            .Callback<string, RedisKey[], RedisValue[]?, CancellationToken>((_, keys, _, _) =>
                releasedKey = keys[0]!)
            .ReturnsAsync(RedisResult.Create((RedisValue)1));

        await sut.ReleaseAsync(userId, orderType, CancellationToken.None);

        Assert.Equal(expectedKey, releasedKey);
    }

    [Fact]
    public async Task TryAcquire_AfterRelease_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var key = $"{InstanceName}:pending-orders:normal:{userId:D}";
        SetupAcquireSequence(key, 1, 0, 1);

        var sut = CreateSut();
        Assert.True((await sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsSuccess);
        Assert.True((await sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsFailure);

        await sut.ReleaseAsync(userId, OrderType.Normal, CancellationToken.None);

        Assert.True((await sut.TryAcquireAsync(userId, OrderType.Normal, CancellationToken.None)).IsSuccess);
    }

    private void SetupAcquire(string key, long returnValue)
    {
        _cache
            .Setup(c => c.EvalAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k.Length == 1 && (string)k[0]! == key),
                It.Is<RedisValue[]?>(a => a != null && a.Length == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)returnValue));
    }

    private void SetupAcquireSequence(string key, params long[] returnValues)
    {
        var sequence = _cache
            .SetupSequence(c => c.EvalAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k.Length == 1 && (string)k[0]! == key),
                It.Is<RedisValue[]?>(a => a != null && a.Length == 2),
                It.IsAny<CancellationToken>()));

        foreach (var value in returnValues)
            sequence.ReturnsAsync(RedisResult.Create((RedisValue)value));
    }
}
