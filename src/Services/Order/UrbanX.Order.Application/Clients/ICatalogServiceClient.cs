using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Clients;

public record CatalogProductValidationDto(
    Guid ProductId,
    bool Exists,
    bool IsActive
);

public record CatalogPriceValidationDto(
    Guid VariantId,
    decimal CurrentPrice
);

public interface ICatalogServiceClient
{
    Task<Result<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>> ValidateProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>> GetCurrentPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default);
}
