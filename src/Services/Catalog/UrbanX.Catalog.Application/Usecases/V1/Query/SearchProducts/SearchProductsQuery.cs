using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.SearchProducts;

[AllowAnonymous]
public record SearchProductsQuery(
    string Q,
    Guid? CategoryId = null,
    decimal? PriceMin = null,
    decimal? PriceMax = null,
    string Sort = "relevance",
    int Page = 1,
    int PageSize = 20) : IQuery<PageResult<ProductSearchResult>>;

public sealed class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    private static readonly HashSet<string> ValidSorts =
        ["relevance", "price_asc", "price_desc", "newest"];

    public SearchProductsQueryValidator()
    {
        RuleFor(x => x.Q).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.PriceMin).GreaterThanOrEqualTo(0).When(x => x.PriceMin.HasValue);
        RuleFor(x => x.PriceMax)
            .GreaterThan(0).When(x => x.PriceMax.HasValue)
            .GreaterThanOrEqualTo(x => x.PriceMin!.Value)
                .When(x => x.PriceMin.HasValue && x.PriceMax.HasValue)
                .WithMessage("price_max must be >= price_min");
        RuleFor(x => x.Sort)
            .Must(s => ValidSorts.Contains(s))
            .WithMessage("sort must be one of: relevance, price_asc, price_desc, newest");
        RuleFor(x => x.Page)
            .Must((q, page) => page * q.PageSize <= 200)
            .WithMessage("Search results are capped at 200 items. Refine your query or use filters.");
    }
}

public record ProductSearchResult(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    decimal BasePrice,
    string? PrimaryImageUrl,
    List<string> Tags);
