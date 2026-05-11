using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductList;

[AllowAnonymous]
public record GetProductListQuery(
    Guid? SellerId = null,
    Guid? CategoryId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PageResult<ProductSummaryResponse>>;

public sealed class GetProductListQueryValidator : AbstractValidator<GetProductListQuery>
{
    public GetProductListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public record ProductSummaryResponse(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    decimal BasePrice,
    string? PrimaryImageUrl,
    List<string> Tags,
    DateTimeOffset UpdatedAt);
