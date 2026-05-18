using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.Errors;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

internal sealed class GetVariantsBatchInternalQueryHandler(IProductRepository productRepository)
    : IQueryHandler<GetVariantsBatchInternalQuery, GetVariantsBatchInternalResponse>
{
    public async Task<Result<GetVariantsBatchInternalResponse>> Handle(
        GetVariantsBatchInternalQuery request,
        CancellationToken cancellationToken)
    {
        var variantIds = request.VariantIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (variantIds.Length == 0)
        {
            return Result.Success(new GetVariantsBatchInternalResponse(Array.Empty<CatalogVariantBatchItem>()));
        }

        var snapshots = await productRepository.GetVariantsByIdsAsync(variantIds, cancellationToken);

        if (snapshots.Count < variantIds.Length)
        {
            var found = snapshots.Select(static s => s.VariantId).ToHashSet();
            var missing = variantIds.First(id => !found.Contains(id));
            return Result.Failure<GetVariantsBatchInternalResponse>(CatalogErrors.VariantNotFound(missing));
        }

        var items = snapshots
            .Select(static s => new CatalogVariantBatchItem(
                s.ProductId,
                s.ProductName,
                s.ProductIsActive,
                s.VariantId,
                s.Sku,
                s.VariantName,
                s.VariantIsActive,
                s.CurrentPrice,
                s.SellerId,
                s.SellerName,
                s.SellerIsActive,
                s.ImageUrl))
            .ToArray();

        return Result.Success(new GetVariantsBatchInternalResponse(items));
    }
}
