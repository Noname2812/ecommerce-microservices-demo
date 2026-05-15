using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain.ReadModels;

namespace UrbanX.Catalog.Domain;

public interface IProductReadRepository
{
    Task<ProductDetailView?> GetByIdAsync(Guid productId, CancellationToken ct = default);

    Task<Result<CursorPageResult<ProductListView>>> GetPageKeysetAsync(
        Guid? sellerId,
        Guid? categoryId,
        string? status,
        string? cursor,
        int pageSize,
        CancellationToken ct = default);

    Task<PageResult<ProductListView>> SearchAsync(
        string q,
        Guid? categoryId,
        decimal? priceMin,
        decimal? priceMax,
        string sort,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
