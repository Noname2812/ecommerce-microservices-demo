using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Query;

[AllowAnonymous]
public record PreviewDiscountQuery(
    string? CouponCode,
    Guid CustomerId,
    decimal Subtotal,
    IReadOnlyList<PreviewOrderItem> Items
) : IQuery<PreviewDiscountResponse>;

public record PreviewOrderItem(Guid ProductId, Guid VariantId, decimal UnitPrice, int Quantity);

public sealed class PreviewDiscountQueryValidator : AbstractValidator<PreviewDiscountQuery>
{
    public PreviewDiscountQueryValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Subtotal).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.CouponCode).MaximumLength(100).When(x => x.CouponCode is not null);
    }
}
