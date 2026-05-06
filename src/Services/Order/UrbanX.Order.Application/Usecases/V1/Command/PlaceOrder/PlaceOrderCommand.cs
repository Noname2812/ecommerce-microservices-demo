using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Orders.Write, MinScope = PermissionScope.Own)]
public record PlaceOrderCommand(
    Guid UserId,
    PlaceOrderShippingAddressDto ShippingAddress,
    decimal ShippingFee,
    string? CouponCode,
    decimal CouponDiscount,
    string? CustomerNote,
    string IdempotencyKey,
    PlaceOrderPricingSnapshotDto PricingSnapshot,
    IReadOnlyList<PlaceOrderLineDto> Items,
    string? CustomerEmail = null
) : ICommand<Guid>;

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
    private const string PhoneRegex = @"^\+?[0-9]{8,15}$";
    private const string CouponCodeRegex = @"^[A-Za-z0-9-]{1,64}$";

    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.PricingSnapshot).NotNull();
        RuleFor(x => x.ShippingFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CouponDiscount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Order must have at least one item")
            .Must(items => items.Count <= 20)
            .WithMessage("Order cannot contain more than 20 items");

        RuleFor(x => x.CouponCode)
            .Matches(CouponCodeRegex)
            .When(x => !string.IsNullOrWhiteSpace(x.CouponCode))
            .WithMessage("Coupon code must be 1-64 characters using letters, digits, or dash.");

        RuleFor(x => x.PricingSnapshot.CapturedAt)
            .Must(capturedAt => capturedAt >= DateTimeOffset.UtcNow.AddMinutes(-30))
            .When(x => x.PricingSnapshot is not null)
            .WithMessage("Pricing snapshot must be captured within the last 30 minutes.");

        RuleFor(x => x.CustomerEmail)
            .EmailAddress()
            .MaximumLength(320)
            .When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));

        When(x => x.ShippingAddress is not null, () =>
        {
            RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(255);
            RuleFor(x => x.ShippingAddress.Phone)
                .NotEmpty()
                .Matches(PhoneRegex)
                .WithMessage("Shipping phone format is invalid.");
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
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 100);
            item.RuleFor(i => i.DiscountAmount).GreaterThanOrEqualTo(0);
        });
    }
}
