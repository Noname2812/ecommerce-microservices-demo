using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

[AllowAnonymous]
public sealed record GetVariantsBatchInternalQuery(
    IReadOnlyCollection<Guid> VariantIds
) : IQuery<GetVariantsBatchInternalResponse>;

public sealed record GetVariantsBatchInternalResponse(
    IReadOnlyList<CatalogVariantBatchItem> Items
);

public sealed record CatalogVariantBatchItem(
    Guid ProductId,
    string ProductName,
    bool ProductIsActive,
    Guid VariantId,
    string Sku,
    string? VariantName,
    bool VariantIsActive,
    decimal CurrentPrice,
    Guid SellerId,
    string SellerName,
    bool SellerIsActive,
    string? ImageUrl);

public sealed class GetVariantsBatchInternalQueryValidator : AbstractValidator<GetVariantsBatchInternalQuery>
{
    public GetVariantsBatchInternalQueryValidator()
    {
        RuleFor(x => x.VariantIds)
            .NotNull()
            .Must(ids => ids.Count <= 100)
            .WithMessage("VariantIds cannot exceed 100 items.");
    }
}
