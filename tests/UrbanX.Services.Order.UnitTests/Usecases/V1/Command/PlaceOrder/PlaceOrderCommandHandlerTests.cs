using Moq;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Services.Order.UnitTests.Usecases.V1.Command.PlaceOrder;

public class PlaceOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IUserContext> _userContext = new();
    private readonly Mock<IPromotionServiceClient> _promotion = new();
    private readonly Mock<IProductValidator> _productValidator = new();
    private readonly Mock<IShippingValidator> _shippingValidator = new();
    private readonly Mock<IPricingValidator> _pricingValidator = new();

    [Fact]
    public async Task Handle_WhenOneBusinessRuleFails_ReturnsWithoutWaitingOtherRules()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _orders.Setup(x => x.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UrbanX.Order.Domain.Models.Order?)null);
        _userContext.SetupGet(x => x.UserId).Returns(userId);
        _productValidator.Setup(x => x.ValidateAsync(It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("PRODUCT_UNAVAILABLE", "inactive")));
        _shippingValidator.Setup(x => x.ValidateAsync(It.IsAny<PlaceOrderShippingAddressDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (PlaceOrderShippingAddressDto _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return Result.Success();
            });
        _pricingValidator.Setup(x => x.ValidateAsync(
                It.IsAny<PlaceOrderPricingSnapshotDto>(),
                It.IsAny<IReadOnlyList<PlaceOrderLineDto>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (PlaceOrderPricingSnapshotDto _, IReadOnlyList<PlaceOrderLineDto> _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return Result.Success();
            });

        var handler = new PlaceOrderCommandHandler(
            _orders.Object,
            _outbox.Object,
            _userContext.Object,
            _promotion.Object,
            _productValidator.Object,
            _shippingValidator.Object,
            _pricingValidator.Object);

        // Act
        var startedAt = DateTimeOffset.UtcNow;
        var result = await handler.Handle(ValidCommand(userId), CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("PRODUCT_UNAVAILABLE", result.Error.Code);
        Assert.True(elapsed < TimeSpan.FromSeconds(1.5));
        _promotion.Verify(
            x => x.RedeemAsync(It.IsAny<PromotionRedeemRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PlaceOrderCommand ValidCommand(Guid userId) => new(
        UserId: userId,
        ShippingAddress: new PlaceOrderShippingAddressDto(
            FullName: "UrbanX User",
            Phone: "+84987654321",
            Address: "123 Main St",
            Ward: null,
            District: "District 1",
            City: "HoChiMinh",
            Province: null,
            Country: "VN",
            ZipCode: null),
        ShippingFee: 10_000,
        CouponCode: null,
        CouponDiscount: 0,
        CustomerNote: null,
        IdempotencyKey: Guid.NewGuid().ToString(),
        PricingSnapshot: new PlaceOrderPricingSnapshotDto(DateTimeOffset.UtcNow),
        Items:
        [
            new PlaceOrderLineDto(
                Guid.NewGuid(), "P", "p",
                Guid.NewGuid(), "SKU", "Default",
                Guid.NewGuid(), "Seller", 100_000, 1, 0, null)
        ]);
}
