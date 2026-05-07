using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public sealed class GetVariantPricesInternalQueryHandler(IProductRepository productRepository)
    : IQueryHandler<GetVariantPricesInternalQuery, GetVariantPricesInternalResponse>
{
    public async Task<Result<GetVariantPricesInternalResponse>> Handle(
        GetVariantPricesInternalQuery request,
        CancellationToken cancellationToken)
    {
        var variantIds = request.VariantIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (variantIds.Length == 0)
        {
            return Result.Success(new GetVariantPricesInternalResponse(Array.Empty<InternalVariantPriceItem>()));
        }

        var prices = await productRepository.GetPricesByVariantIdsAsync(variantIds, cancellationToken);
        var items = prices
            .Select(x => new InternalVariantPriceItem(x.Key, x.Value))
            .ToArray();

        return Result.Success(new GetVariantPricesInternalResponse(items));
    }
}
