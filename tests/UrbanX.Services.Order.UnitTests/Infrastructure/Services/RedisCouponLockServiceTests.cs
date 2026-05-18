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

public sealed class RedisCouponLockServiceTests
{
    private const string InstanceName = "test-order";
    private readonly Mock<ICacheService> _cache = new();

    private RedisCouponLockService CreateSut()
    {
        var placeOptions = new Mock<IOptionsMonitor<PlaceOrderOptions>>();
        placeOptions.Setup(x => x.CurrentValue).Returns(new PlaceOrderOptions { CouponLockTtlSeconds = 960 });
        return new RedisCouponLockService(
            _cache.Object,
            Options.Create(new CacheOptions { InstanceName = InstanceName }),
            placeOptions.Object,
            NullLogger<RedisCouponLockService>.Instance);
    }

    [Theory]
    [InlineData(-1L, "Order.CouponAlreadyUsed")]
    [InlineData(-2L, "Order.CouponNotEligible")]
    [InlineData(-3L, "Order.CouponConcurrentClaim")]
    [InlineData(-4L, "Order.CouponExhausted")]
    public async Task TryLock_LuaErrorCode_MapsToExpectedError(long luaCode, string expectedCode)
    {
        SetupReserve(luaCode);

        var result = await CreateSut().TryLockAsync("SAVE10", Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public async Task TryLock_Success_ParsesAmountAndDiscountType()
    {
        SetupReserve("50000|PERCENT");

        var result = await CreateSut().TryLockAsync("SAVE10", Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50_000m, result.Value!.DiscountAmount);
        Assert.Equal("PERCENT", result.Value.DiscountType);
    }

    [Fact]
    public async Task Release_RedisThrows_DoesNotPropagate()
    {
        _cache
            .Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        await CreateSut().ReleaseAsync("SAVE10", Guid.NewGuid(), CancellationToken.None);
    }

    private void SetupReserve(long luaCode) =>
        _cache
            .Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)luaCode));

    private void SetupReserve(string bulkPayload) =>
        _cache
            .Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)bulkPayload));
}
