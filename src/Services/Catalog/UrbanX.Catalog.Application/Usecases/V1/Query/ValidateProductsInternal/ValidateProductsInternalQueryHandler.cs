using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain;
using UrbanX.Catalog.Domain.ValueObjects;

namespace UrbanX.Catalog.Application.Usecases.V1.Query;

public sealed class ValidateProductsInternalQueryHandler(IProductRepository productRepository)
    : IQueryHandler<ValidateProductsInternalQuery, ValidateProductsInternalResponse>
{
    public async Task<Result<ValidateProductsInternalResponse>> Handle(
        ValidateProductsInternalQuery request,
        CancellationToken cancellationToken)
    {
        var productIds = request.ProductIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (productIds.Length == 0)
        {
            return Result.Success(new ValidateProductsInternalResponse(Array.Empty<InternalProductValidationItem>()));
        }

        var statuses = await productRepository.GetStatusesByProductIdsAsync(productIds, cancellationToken);

        var items = productIds
            .Select(id =>
            {
                if (!statuses.TryGetValue(id, out var status))
                {
                    return new InternalProductValidationItem(id, Exists: false, IsActive: false);
                }

                return new InternalProductValidationItem(
                    id,
                    Exists: true,
                    IsActive: string.Equals(status, ProductStatus.Active, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        return Result.Success(new ValidateProductsInternalResponse(items));
    }
}
