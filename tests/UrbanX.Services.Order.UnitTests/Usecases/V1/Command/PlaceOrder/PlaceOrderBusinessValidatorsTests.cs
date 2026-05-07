using Moq;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Usecases.V1.Command.PlaceOrder;

public class PlaceOrderBusinessValidatorsTests
{
    private readonly Mock<ICatalogServiceClient> _catalogClient = new();

    [Fact]
    public async Task ProductValidator_WhenAllProductsExistAndActive_ReturnsSuccess()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var validator = new ProductValidator(_catalogClient.Object);
        _catalogClient
            .Setup(x => x.ValidateProductsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shared.Kernel.Primitives.Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(
                new Dictionary<Guid, CatalogProductValidationDto>
                {
                    [productId] = new(productId, true, true)
                }));

        // Act
        var result = await validator.ValidateAsync([ValidItem(productId: productId)], CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ProductValidator_WhenProductMissing_ReturnsFailure()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var validator = new ProductValidator(_catalogClient.Object);
        _catalogClient
            .Setup(x => x.ValidateProductsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shared.Kernel.Primitives.Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(
                new Dictionary<Guid, CatalogProductValidationDto>()));

        // Act
        var result = await validator.ValidateAsync([ValidItem(productId: productId)], CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("PRODUCT_NOT_FOUND", result.Error.Code);
    }

    [Fact]
    public async Task ProductValidator_WhenProductInactive_ReturnsFailure()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var validator = new ProductValidator(_catalogClient.Object);
        _catalogClient
            .Setup(x => x.ValidateProductsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shared.Kernel.Primitives.Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(
                new Dictionary<Guid, CatalogProductValidationDto>
                {
                    [productId] = new(productId, true, false)
                }));

        // Act
        var result = await validator.ValidateAsync([ValidItem(productId: productId)], CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("PRODUCT_UNAVAILABLE", result.Error.Code);
    }

    [Fact]
    public async Task ShippingValidator_WhenRegionSupported_ReturnsSuccess()
    {
        // Arrange
        var validator = new ShippingValidator();

        // Act
        var result = await validator.ValidateAsync(
            new PlaceOrderShippingAddressDto("U", "+84987654321", "A", null, "District 1", "HoChiMinh", null, "VN", null),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ShippingValidator_WhenRegionUnsupported_ReturnsFailure()
    {
        // Arrange
        var validator = new ShippingValidator();

        // Act
        var result = await validator.ValidateAsync(
            new PlaceOrderShippingAddressDto("U", "+84987654321", "A", null, "Unknown", "Unknown", null, "VN", null),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("SHIPPING_NOT_AVAILABLE", result.Error.Code);
    }

    [Fact]
    public async Task PricingValidator_WhenWithinTolerance_ReturnsSuccess()
    {
        // Arrange
        var variantId = Guid.NewGuid();
        var validator = new PricingValidator(_catalogClient.Object);
        _catalogClient
            .Setup(x => x.GetCurrentPricesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shared.Kernel.Primitives.Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(
                new Dictionary<Guid, CatalogPriceValidationDto>
                {
                    [variantId] = new(variantId, 100_500)
                }));

        // Act
        var result = await validator.ValidateAsync(
            new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow),
            [ValidItem(variantId: variantId, unitPrice: 100_000)],
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PricingValidator_WhenOutsideTolerance_ReturnsPriceMismatch()
    {
        // Arrange
        var variantId = Guid.NewGuid();
        var validator = new PricingValidator(_catalogClient.Object);
        _catalogClient
            .Setup(x => x.GetCurrentPricesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shared.Kernel.Primitives.Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(
                new Dictionary<Guid, CatalogPriceValidationDto>
                {
                    [variantId] = new(variantId, 120_000)
                }));

        // Act
        var result = await validator.ValidateAsync(
            new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow),
            [ValidItem(variantId: variantId, unitPrice: 100_000)],
            CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("PRICE_MISMATCH", result.Error.Code);
    }

    private static PlaceOrderLineDto ValidItem(
        Guid? productId = null,
        Guid? variantId = null,
        decimal unitPrice = 100_000) => new(
        ProductId: productId ?? Guid.NewGuid(),
        ProductName: "P",
        ProductSlug: "p",
        VariantId: variantId ?? Guid.NewGuid(),
        VariantSku: "SKU",
        VariantName: "Default",
        SellerId: Guid.NewGuid(),
        SellerName: "Seller",
        UnitPrice: unitPrice,
        Quantity: 1,
        DiscountAmount: 0,
        ImageUrl: null);
}
