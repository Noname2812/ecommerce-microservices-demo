using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Clients;

public sealed record CatalogProductValidationDto(
    Guid ProductId,
    bool Exists,
    bool IsActive
);

public sealed record CatalogPriceValidationDto(
    Guid VariantId,
    decimal CurrentPrice
);

public sealed record CatalogVariantInfo(
    Guid ProductId,
    string ProductName,
    bool ProductIsActive,
    Guid VariantId,
    string Sku,
    string? VariantName,
    bool VariantIsActive,
    decimal CurrentPrice,
    Guid SellerId,
    string SellerName,
    bool SellerIsActive,
    string? ImageUrl);

public interface ICatalogServiceClient
{
    Task<Result<IReadOnlyList<CatalogVariantInfo>>> GetVariantsAsync(
        IEnumerable<Guid> variantIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>> ValidateProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>> GetCurrentPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default);
}
