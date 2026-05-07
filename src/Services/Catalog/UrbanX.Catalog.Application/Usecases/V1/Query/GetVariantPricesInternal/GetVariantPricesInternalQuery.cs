using FluentValidation;
using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public sealed record GetVariantPricesInternalQuery(
    IReadOnlyCollection<Guid> VariantIds
) : IQuery<GetVariantPricesInternalResponse>;

public sealed record GetVariantPricesInternalResponse(
    IReadOnlyList<InternalVariantPriceItem> Items
);

public sealed record InternalVariantPriceItem(
    Guid VariantId,
    decimal CurrentPrice
);

public sealed class GetVariantPricesInternalQueryValidator : AbstractValidator<GetVariantPricesInternalQuery>
{
    public GetVariantPricesInternalQueryValidator()
    {
        RuleFor(x => x.VariantIds)
            .NotNull()
            .Must(ids => ids.Count <= 1000)
            .WithMessage("VariantIds cannot exceed 1000 items.");
    }
}
