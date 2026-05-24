using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Order.Application.Usecases.V1.Command.RefreshProductVariantProjection;

[AllowAnonymous]
public record RefreshProductVariantProjectionCommand(
    Guid VariantId,
    Guid ProductId,
    string ProductName,
    bool ProductIsActive,
    string Sku,
    string? VariantName,
    decimal Price,
    bool IsActive,
    Guid SellerId,
    string SellerName,
    bool SellerIsActive,
    string? ImageUrl,
    int RowVersion
) : ICommand;

public sealed class RefreshProductVariantProjectionCommandValidator
    : AbstractValidator<RefreshProductVariantProjectionCommand>
{
    public RefreshProductVariantProjectionCommandValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ProductName).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SellerId).NotEmpty();
        RuleFor(x => x.SellerName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}
