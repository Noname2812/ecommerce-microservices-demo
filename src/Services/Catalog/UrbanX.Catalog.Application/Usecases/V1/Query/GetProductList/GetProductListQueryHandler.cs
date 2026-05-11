using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductList;

public sealed class GetProductListQueryHandler(IProductReadRepository repo)
    : IQueryHandler<GetProductListQuery, PageResult<ProductSummaryResponse>>
{
    public async Task<Result<PageResult<ProductSummaryResponse>>> Handle(
        GetProductListQuery request, CancellationToken ct)
    {
        var page = await repo.GetPageAsync(
            request.SellerId, request.CategoryId, request.Status,
            request.Page, request.PageSize, ct);

        var items = page.Items
            .Select(v => new ProductSummaryResponse(
                v.ProductId, v.Sku, v.Name, v.Slug, v.Status,
                v.CategoryId, v.CategoryName, v.BrandId, v.BrandName,
                v.BasePrice, v.PrimaryImageUrl, v.Tags.ToList(), v.UpdatedAt))
            .ToList();

        return Result.Success(
            PageResult<ProductSummaryResponse>.Create(items, page.PageIndex, page.PageSize, page.TotalCount));
    }
}
