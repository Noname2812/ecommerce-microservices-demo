using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Infrastructure.Services;

internal sealed class CatalogServiceClient(HttpClient httpClient, ILogger<CatalogServiceClient> logger) : ICatalogServiceClient
{
    private static readonly Error Unavailable = OrderErrors.CatalogUnavailable;

    public async Task<Result<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>> ValidateProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                "/api/v1/catalog/internal/validate-products",
                new ProductValidationRequest(productIds),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(Unavailable);

            var payload = await response.Content.ReadFromJsonAsync<ProductValidationResponse>(cancellationToken);
            var data = (payload?.Items ?? [])
                .ToDictionary(x => x.ProductId, x => x);

            return Result.Success<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog HTTP fallback failed (validate-products)");
            return Result.Failure<IReadOnlyDictionary<Guid, CatalogProductValidationDto>>(Unavailable);
        }
    }

    public async Task<Result<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>> GetCurrentPricesAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                "/api/v1/catalog/internal/variant-prices",
                new VariantPriceRequest(variantIds),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(Unavailable);

            var payload = await response.Content.ReadFromJsonAsync<VariantPriceResponse>(cancellationToken);
            var data = (payload?.Items ?? [])
                .ToDictionary(x => x.VariantId, x => x);

            return Result.Success<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog HTTP fallback failed (variant-prices)");
            return Result.Failure<IReadOnlyDictionary<Guid, CatalogPriceValidationDto>>(Unavailable);
        }
    }

    private sealed record ProductValidationRequest(IReadOnlyCollection<Guid> ProductIds);
    private sealed record ProductValidationResponse(IReadOnlyList<CatalogProductValidationDto> Items);
    private sealed record VariantPriceRequest(IReadOnlyCollection<Guid> VariantIds);
    private sealed record VariantPriceResponse(IReadOnlyList<CatalogPriceValidationDto> Items);
}
