using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductList;

public sealed class GetProductListQueryHandler(IProductReadRepository repo)
    : IQueryHandler<GetProductListQuery, CursorPageResult<ProductSummaryResponse>>
{
    public async Task<Result<CursorPageResult<ProductSummaryResponse>>> Handle(
        GetProductListQuery request, CancellationToken ct)
    {
        var page = await repo.GetPageKeysetAsync(
            request.SellerId, request.CategoryId, request.Status,
            request.Cursor, request.PageSize, ct);

        var items = page.Items
            .Select(v => new ProductSummaryResponse(
                v.ProductId, v.Sku, v.Name, v.Slug, v.Status,
                v.CategoryId, v.CategoryName, v.BrandId, v.BrandName,
                v.BasePrice, v.PrimaryImageUrl, v.Tags.ToList(), v.UpdatedAt))
            .ToList();

        return Result.Success(CursorPageResult<ProductSummaryResponse>.Create(items, page.NextCursor));
    }
}
