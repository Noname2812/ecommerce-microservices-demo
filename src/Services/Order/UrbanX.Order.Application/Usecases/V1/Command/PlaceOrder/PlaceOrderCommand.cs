using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Order.Application.Usecases.V1.Command.Common;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

[RequirePermission(Permissions.Orders.Write, MinScope = PermissionScope.Own)]
public record PlaceOrderCommand(
    PlaceOrderShippingAddressDto ShippingAddress,
    decimal ShippingFee,
    string? CouponCode,
    string? CustomerNote,
    string IdempotencyKey,
    PlaceOrderPricingSnapshotDto PricingSnapshot,
    IReadOnlyList<PlaceOrderLineDto> Items,
    string? CustomerEmail = null
) : ICommand<Guid>, IPlaceOrderRequest;

public record PlaceOrderShippingAddressDto(
    string FullName,
    string Phone,
    string Address,
    string? Ward,
    string District,
    string City,
    string? Province,
    string Country,
    string? ZipCode
);

public record PlaceOrderPricingSnapshotDto(
    DateTimeOffset CapturedAt
);

public record PlaceOrderLineDto(
    Guid ProductId,
    string ProductName,
    string? ProductSlug,
    Guid VariantId,
    string VariantSku,
    string? VariantName,
    Guid SellerId,
    string SellerName,
    decimal UnitPrice,
    int Quantity,
    decimal DiscountAmount,
    string? ImageUrl
);

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    private const int MaxItems = 20;
    private const int MaxQtyPerItem = 100;

    public PlaceOrderCommandValidator()
    {
        this.RuleForShippingAddress();
        this.RuleForShippingFee();
        this.RuleForIdempotencyKey();
        this.RuleForCouponCode();
        this.RuleForCustomerEmail();
        this.RuleForPricingSnapshot();
        this.RuleForItems(
            MaxItems,
            MaxQtyPerItem,
            itemsCountMessage: $"Order cannot contain more than {MaxItems} items.");
    }
}
