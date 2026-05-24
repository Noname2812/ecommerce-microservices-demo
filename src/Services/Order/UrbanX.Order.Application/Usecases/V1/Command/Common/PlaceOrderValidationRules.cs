using FluentValidation;

namespace UrbanX.Order.Application.Usecases.V1.Command.Common;

internal static class PlaceOrderValidationRules
{
    public const string PhoneRegex = @"^\+?[0-9]{8,15}$";
    public const string CouponCodeRegex = @"^[A-Za-z0-9-]{1,64}$";
    public const string CouponHoldTokenRegex = @"^[A-Za-z0-9]{32}$";

    public static void RuleForShippingAddress<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
    {
        validator.RuleFor(x => x.ShippingAddress).NotNull();

        validator.When(x => x.ShippingAddress is not null, () =>
        {
            validator.RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(255);
            validator.RuleFor(x => x.ShippingAddress.Phone)
                .NotEmpty()
                .Matches(PhoneRegex)
                .WithMessage("Shipping phone format is invalid.");
            validator.RuleFor(x => x.ShippingAddress.Address).NotEmpty().MaximumLength(255);
            validator.RuleFor(x => x.ShippingAddress.District).NotEmpty().MaximumLength(100);
            validator.RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(100);
            validator.RuleFor(x => x.ShippingAddress.Country).NotEmpty().MaximumLength(100);
        });
    }

    public static void RuleForShippingFee<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.ShippingFee).GreaterThanOrEqualTo(0);

    public static void RuleForIdempotencyKey<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .Must(key => Guid.TryParseExact(key, "D", out _))
            .WithMessage("IdempotencyKey must be a valid UUID in format xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.");

    public static void RuleForCouponCode<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.CouponCode)
            .Matches(CouponCodeRegex)
            .When(x => !string.IsNullOrWhiteSpace(x.CouponCode))
            .WithMessage("Coupon code must be 1-64 characters using letters, digits, or dash.");

    public static void RuleForCouponHoldToken<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.CouponHoldToken)
            .Matches(CouponHoldTokenRegex)
            .When(x => !string.IsNullOrWhiteSpace(x.CouponHoldToken))
            .WithMessage("CouponHoldToken must be a 32-char hex string issued by /api/v1/promotion/coupon-holds.");

    public static void RuleForCustomerEmail<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.CustomerEmail)
            .EmailAddress()
            .MaximumLength(320)
            .When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));

    public static void RuleForPricingSnapshot<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.PricingSnapshot).NotNull();

    public static void RuleForPaymentMethod<T>(this AbstractValidator<T> validator)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("PaymentMethod must be either 'Sepay' or 'Momo'.");

    public static void RuleForPricingWindow<T>(
        this AbstractValidator<T> validator,
        TimeSpan window,
        string message)
        where T : IPlaceOrderRequest
        => validator.RuleFor(x => x.PricingSnapshot.CapturedAt)
            .Must(captured => DateTimeOffset.UtcNow - captured <= window)
            .When(x => x.PricingSnapshot is not null)
            .WithMessage(message);

    public static void RuleForItems<T>(
        this AbstractValidator<T> validator,
        int maxItems,
        int maxQtyPerItem,
        string? itemsCountMessage = null,
        string? itemQtyMessage = null)
        where T : IPlaceOrderRequest
    {
        validator.RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Order must have at least one item.")
            .Must(items => items.Count <= maxItems)
            .WithMessage(itemsCountMessage ?? $"Order cannot contain more than {maxItems} items.");

        validator.RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.VariantId).NotEmpty();
            item.RuleFor(i => i.VariantSku).NotEmpty().MaximumLength(100);
            item.RuleFor(i => i.ProductName).NotEmpty().MaximumLength(500);
            item.RuleFor(i => i.SellerId).NotEmpty();
            item.RuleFor(i => i.SellerName).NotEmpty().MaximumLength(255);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
            item.RuleFor(i => i.Quantity)
                .InclusiveBetween(1, maxQtyPerItem)
                .WithMessage(itemQtyMessage ?? $"Each item quantity must be between 1 and {maxQtyPerItem}.");
            item.RuleFor(i => i.Version)
                .GreaterThan(0)
                .WithMessage("Each item must include the variant Version returned by the catalog read API.");
        });
    }
}
