using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Orders.Write, MinScope = PermissionScope.Own)]
public record PlaceOrderCommand(
    string RecipientName,
    string RecipientPhone,
    string Street,
    string? Ward,
    string District,
    string City,
    string? Province,
    string Country,
    string? ZipCode,
    decimal ShippingFee,
    string? CouponCode,
    decimal CouponDiscount,
    string? CustomerNote,
    string IdempotencyKey,
    IReadOnlyList<PlaceOrderLineDto> Items
) : ICommand<Guid>;

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
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.RecipientName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.RecipientPhone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(255);
        RuleFor(x => x.District).NotEmpty().MaximumLength(100);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShippingFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CouponDiscount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.VariantId).NotEmpty();
            item.RuleFor(i => i.VariantSku).NotEmpty().MaximumLength(100);
            item.RuleFor(i => i.ProductName).NotEmpty().MaximumLength(500);
            item.RuleFor(i => i.SellerId).NotEmpty();
            item.RuleFor(i => i.SellerName).NotEmpty().MaximumLength(255);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.DiscountAmount).GreaterThanOrEqualTo(0);
        });
    }
}
