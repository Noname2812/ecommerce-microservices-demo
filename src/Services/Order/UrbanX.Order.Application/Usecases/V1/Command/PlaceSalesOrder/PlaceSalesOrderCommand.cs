using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.Common;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

[RequirePermission(Permissions.Orders.Write, MinScope = PermissionScope.Own)]
public record PlaceSalesOrderCommand(
    Guid CampaignId,
    PlaceOrderShippingAddressDto ShippingAddress,
    decimal ShippingFee,
    string? CouponCode,
    string? CustomerNote,
    string IdempotencyKey,
    PlaceOrderPricingSnapshotDto PricingSnapshot,
    decimal ExpectedTotal,
    IReadOnlyList<PlaceOrderLineDto> Items,
    string? CustomerEmail = null
) : ICommand<Guid>, IIdempotentCommand, IPlaceOrderRequest
{
    TimeSpan? IIdempotentCommand.IdempotencyTtl => TimeSpan.FromHours(24);
}

public sealed class PlaceSalesOrderCommandValidator : AbstractValidator<PlaceSalesOrderCommand>
{
    private const int MaxItems = 10;
    private const int MaxQtyPerItem = 5;

    public PlaceSalesOrderCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty().WithMessage("CampaignId is required for sales orders.");
        RuleFor(x => x.ExpectedTotal).GreaterThan(0);

        this.RuleForShippingAddress();
        this.RuleForShippingFee();
        this.RuleForIdempotencyKey();
        this.RuleForCouponCode();
        this.RuleForCustomerEmail();
        this.RuleForPricingSnapshot();
        this.RuleForItems(
            MaxItems,
            MaxQtyPerItem,
            itemsCountMessage: $"Sales order cannot contain more than {MaxItems} items.",
            itemQtyMessage:    $"Each item quantity cannot exceed {MaxQtyPerItem} for sales orders.");
    }
}
