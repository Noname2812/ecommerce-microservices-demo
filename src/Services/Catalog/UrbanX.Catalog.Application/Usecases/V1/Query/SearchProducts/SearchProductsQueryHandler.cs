using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.SearchProducts;

public sealed class SearchProductsQueryHandler(IProductReadRepository repo)
    : IQueryHandler<SearchProductsQuery, PageResult<ProductSearchResult>>
{
    public async Task<Result<PageResult<ProductSearchResult>>> Handle(
        SearchProductsQuery request, CancellationToken ct)
    {
        var page = await repo.SearchAsync(
            request.Q, request.CategoryId, request.PriceMin, request.PriceMax,
            request.Sort, request.Page, request.PageSize, ct);

        var items = page.Items
            .Select(v => new ProductSearchResult(
                v.ProductId, v.Name, v.Slug, v.Status,
                v.CategoryId, v.CategoryName, v.BasePrice,
                v.PrimaryImageUrl, v.Tags.ToList()))
            .ToList();

        return Result.Success(
            PageResult<ProductSearchResult>.Create(items, page.PageIndex, page.PageSize, page.TotalCount));
    }
}
