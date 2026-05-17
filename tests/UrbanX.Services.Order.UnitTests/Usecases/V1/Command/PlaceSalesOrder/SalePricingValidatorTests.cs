using Moq;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Promotion;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Services.Order.UnitTests.Usecases.V1.Command.PlaceSalesOrder;

public class SalePricingValidatorTests
{
    private static PlaceOrderLineDto Line(Guid variantId, decimal unitPrice, string sku = "SKU") =>
        new(
            Guid.NewGuid(), "Product", null, variantId, sku, null,
            Guid.NewGuid(), "Seller", unitPrice, 1, 0, null);

    private static Mock<ISaleSnapshotCache> CacheReturning(IReadOnlyDictionary<Guid, decimal> prices)
    {
        var mock = new Mock<ISaleSnapshotCache>();
        mock.Setup(x => x.GetSalePricesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(prices));
        return mock;
    }

    [Fact]
    public async Task ValidateAsync_PartialPriceDict_ReturnsSalePricingUnavailable()
    {
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var cache = CacheReturning(new Dictionary<Guid, decimal> { { v1, 10m } });

        var validator = new SalePricingValidator(cache.Object);
        var items = new[] { Line(v1, 10m), Line(v2, 20m) };
        var snapshot = new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow);

        var result = await validator.ValidateAsync(Guid.NewGuid(), snapshot, items, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderErrors.SalePricingUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ValidateAsync_EmptyPriceDictWithItems_ReturnsSalePricingUnavailable()
    {
        var cache = CacheReturning(new Dictionary<Guid, decimal>());

        var validator = new SalePricingValidator(cache.Object);
        var v = Guid.NewGuid();
        var items = new[] { Line(v, 99m) };
        var snapshot = new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow);

        var result = await validator.ValidateAsync(Guid.NewGuid(), snapshot, items, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderErrors.SalePricingUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ValidateAsync_FullDictMatchingPrices_ReturnsSuccess()
    {
        var v = Guid.NewGuid();
        var cache = CacheReturning(new Dictionary<Guid, decimal> { { v, 100m } });

        var validator = new SalePricingValidator(cache.Object);
        var items = new[] { Line(v, 100m) };
        var snapshot = new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow);

        var result = await validator.ValidateAsync(Guid.NewGuid(), snapshot, items, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
