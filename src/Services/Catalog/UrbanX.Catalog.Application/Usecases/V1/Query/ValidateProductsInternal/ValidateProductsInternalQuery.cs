using FluentValidation;
using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public sealed record ValidateProductsInternalQuery(
    IReadOnlyCollection<Guid> ProductIds
) : IQuery<ValidateProductsInternalResponse>;

public sealed record ValidateProductsInternalResponse(
    IReadOnlyList<InternalProductValidationItem> Items
);

public sealed record InternalProductValidationItem(
    Guid ProductId,
    bool Exists,
    bool IsActive
);

public sealed class ValidateProductsInternalQueryValidator : AbstractValidator<ValidateProductsInternalQuery>
{
    public ValidateProductsInternalQueryValidator()
    {
        RuleFor(x => x.ProductIds)
            .NotNull()
            .Must(ids => ids.Count <= 1000)
            .WithMessage("ProductIds cannot exceed 1000 items.");
    }
}
