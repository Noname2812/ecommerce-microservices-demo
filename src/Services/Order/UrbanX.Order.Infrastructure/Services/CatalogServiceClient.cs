using System.Net.Http.Json;
using Shared.Kernel.Primitives;

namespace UrbanX.Order.Infrastructure.Services;

internal sealed class CatalogServiceClient(HttpClient httpClient) : ICatalogServiceClient
{
    public async Task<Result<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>> ValidateProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/catalog/internal/validate-products",
            new ProductValidationRequest(productIds),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Result.Failure<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(
                new Error("CATALOG_UNAVAILABLE", "Catalog service is unavailable."));

        var payload = await response.Content.ReadFromJsonAsync<ProductValidationResponse>(cancellationToken);
        var data = (payload?.Items ?? [])
            .ToDictionary(x => x.ProductId, x => x);

        return Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(data);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>> GetCurrentPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/catalog/internal/variant-prices",
            new VariantPriceRequest(variantIds),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Result.Failure<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(
                new Error("CATALOG_UNAVAILABLE", "Catalog service is unavailable."));

        var payload = await response.Content.ReadFromJsonAsync<VariantPriceResponse>(cancellationToken);
        var data = (payload?.Items ?? [])
            .ToDictionary(x => x.VariantId, x => x);

        return Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(data);
    }

    private sealed record ProductValidationRequest(IReadOnlyCollection<Guid> ProductIds);
    private sealed record ProductValidationResponse(IReadOnlyList<CatalogProductValidationDto> Items);
    private sealed record VariantPriceRequest(IReadOnlyCollection<Guid> VariantIds);
    private sealed record VariantPriceResponse(IReadOnlyList<CatalogPriceValidationDto> Items);
}
