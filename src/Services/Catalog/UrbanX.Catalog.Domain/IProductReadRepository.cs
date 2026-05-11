using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain.ReadModels;

namespace UrbanX.Catalog.Domain;

public interface IProductReadRepository
{
    Task<ProductDetailView?> GetByIdAsync(Guid productId, CancellationToken ct = default);

    Task<PageResult<ProductListView>> GetPageAsync(
        Guid? sellerId,
        Guid? categoryId,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
