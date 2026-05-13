using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

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
    IReadOnlyList<PlaceOrderLineDto> Items,
    string? CustomerEmail = null
) : ICommand<Guid>, IIdempotentCommand
{
    TimeSpan? IIdempotentCommand.IdempotencyTtl => TimeSpan.FromHours(24);
}

public sealed class PlaceSalesOrderCommandValidator : AbstractValidator<PlaceSalesOrderCommand>
{
    private const string PhoneRegex   = @"^\+?[0-9]{8,15}$";
    private const string CouponRegex  = @"^[A-Za-z0-9-]{1,64}$";
    private const int    MaxItemsPerSalesOrder = 10;
    private const int    MaxQtyPerItem         = 5;
    private static readonly TimeSpan PricingWindow = TimeSpan.FromMinutes(5);

    public PlaceSalesOrderCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty().WithMessage("CampaignId is required for sales orders.");
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.PricingSnapshot).NotNull();
        RuleFor(x => x.ShippingFee).GreaterThanOrEqualTo(0);

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .Must(k => Guid.TryParseExact(k, "D", out _))
            .WithMessage("IdempotencyKey must be a valid UUID.");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Order must have at least one item.")
            .Must(i => i.Count <= MaxItemsPerSalesOrder)
            .WithMessage($"Sales order cannot contain more than {MaxItemsPerSalesOrder} items.");

        RuleFor(x => x.PricingSnapshot.CapturedAt)
            .Must(t => DateTimeOffset.UtcNow - t <= PricingWindow)
            .When(x => x.PricingSnapshot is not null)
            .WithMessage("Pricing snapshot must be captured within the last 5 minutes for sales orders.");

        RuleFor(x => x.CouponCode)
            .Matches(CouponRegex)
            .When(x => !string.IsNullOrWhiteSpace(x.CouponCode));

        RuleFor(x => x.CustomerEmail)
            .EmailAddress().MaximumLength(320)
            .When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));

        When(x => x.ShippingAddress is not null, () =>
        {
            RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(255);
            RuleFor(x => x.ShippingAddress.Phone).NotEmpty().Matches(PhoneRegex);
            RuleFor(x => x.ShippingAddress.Address).NotEmpty().MaximumLength(255);
            RuleFor(x => x.ShippingAddress.District).NotEmpty().MaximumLength(100);
            RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.ShippingAddress.Country).NotEmpty().MaximumLength(100);
        });

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.VariantId).NotEmpty();
            item.RuleFor(i => i.VariantSku).NotEmpty().MaximumLength(100);
            item.RuleFor(i => i.ProductName).NotEmpty().MaximumLength(500);
            item.RuleFor(i => i.SellerId).NotEmpty();
            item.RuleFor(i => i.SellerName).NotEmpty().MaximumLength(255);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
            item.RuleFor(i => i.Quantity)
                .InclusiveBetween(1, MaxQtyPerItem)
                .WithMessage($"Each item quantity cannot exceed {MaxQtyPerItem} for sales orders.");
        });
    }
}
