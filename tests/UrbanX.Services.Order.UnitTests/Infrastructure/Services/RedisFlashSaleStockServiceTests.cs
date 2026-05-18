using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Cache.Abstractions;
using Shared.Cache.DependencyInjection.Options;
using Shared.Kernel.Primitives;
using StackExchange.Redis;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Infrastructure.Services;

public sealed class RedisFlashSaleStockServiceTests
{
    private const string InstanceName = "test-order";
    private readonly Mock<ICacheService> _cache = new();

    private RedisFlashSaleStockService CreateSut() =>
        new(
            _cache.Object,
            Options.Create(new CacheOptions { InstanceName = InstanceName }),
            NullLogger<RedisFlashSaleStockService>.Instance);

    [Fact]
    public async Task TryReserve_StockSufficient_ReturnsSuccess()
    {
        var campaignId = Guid.NewGuid();
        SetupReserve(campaignId, quantity: 2, remaining: 3);

        var result = await CreateSut().TryReserveAsync(campaignId, 2, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TryReserve_StockInsufficient_ReturnsFlashSaleSoldOut()
    {
        var campaignId = Guid.NewGuid();
        SetupReserve(campaignId, quantity: 3, remaining: -1);

        var result = await CreateSut().TryReserveAsync(campaignId, 3, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.FlashSaleSoldOut(campaignId).Code, result.Error.Code);
    }

    [Fact]
    public async Task Restore_RedisThrows_DoesNotPropagate()
    {
        _cache
            .Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        await CreateSut().RestoreAsync(Guid.NewGuid(), 2, CancellationToken.None);
    }

    [Fact]
    public async Task Restore_CallsIncrBy()
    {
        var campaignId = Guid.NewGuid();
        var key = $"{InstanceName}:flashsale:{campaignId:D}:stock";
        _cache
            .Setup(c => c.EvalAsync(
                It.Is<string>(s => s.Contains("INCRBY")),
                It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == key),
                It.Is<RedisValue[]?>(args => args != null && (int)args![0] == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)5));

        await CreateSut().RestoreAsync(campaignId, 2, CancellationToken.None);

        _cache.VerifyAll();
    }

    private void SetupReserve(Guid campaignId, int quantity, long remaining)
    {
        var key = $"{InstanceName}:flashsale:{campaignId:D}:stock";
        _cache
            .Setup(c => c.EvalAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == key),
                It.Is<RedisValue[]?>(args => args != null && (int)args![0] == quantity),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)remaining));
    }
}
