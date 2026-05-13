using Microsoft.Extensions.Options;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Options;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Infrastructure.Services;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class ProductValidator(ICatalogServiceClient catalogServiceClient) : IProductValidator
{
    public async Task<Result> ValidateAsync(IReadOnlyList<PlaceOrderLineDto> items, CancellationToken cancellationToken)
    {
        var productIds = items.Select(x => x.ProductId).Distinct().ToArray();
        var response = await catalogServiceClient.ValidateProductsAsync(productIds, cancellationToken);
        if (response.IsFailure)
            return Result.Failure(response.Error);

        var products = response.Value!;
        foreach (var productId in productIds)
        {
            if (!products.TryGetValue(productId, out var product) || !product.Exists)
                return Result.Failure(OrderErrors.ProductNotFound(productId));

            if (!product.IsActive)
                return Result.Failure(OrderErrors.ProductUnavailable(productId));
        }

        return Result.Success();
    }
}

public sealed class ShippingValidator(IOptions<ShippingOptions> options) : IShippingValidator
{
    private readonly HashSet<string> _supportedRegions =
        new(options.Value.SupportedRegions, StringComparer.OrdinalIgnoreCase);

    public Task<Result> ValidateAsync(PlaceOrderShippingAddressDto shippingAddress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var regionKey = $"{Normalize(shippingAddress.City)}:{Normalize(shippingAddress.District)}";
        if (!_supportedRegions.Contains(regionKey))
            return Task.FromResult(Result.Failure(OrderErrors.ShippingNotAvailable(
                shippingAddress.City,
                shippingAddress.District)));

        return Task.FromResult(Result.Success());
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed class PricingValidator(ICatalogServiceClient catalogServiceClient) : IPricingValidator
{
    private const decimal AllowedTolerance = 0.01m;

    public async Task<Result> ValidateAsync(
        PlaceOrderPricingSnapshotDto snapshot,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken cancellationToken)
    {
        var variantIds = items.Select(x => x.VariantId).Distinct().ToArray();
        var response = await catalogServiceClient.GetCurrentPricesAsync(variantIds, cancellationToken);
        if (response.IsFailure)
            return Result.Failure(response.Error);

        var currentPrices = response.Value!;

        foreach (var item in items)
        {
            if (!currentPrices.TryGetValue(item.VariantId, out var current))
                return Result.Failure(OrderErrors.ProductNotFound(item.ProductId));

            var lowerBound = item.UnitPrice * (1 - AllowedTolerance);
            var upperBound = item.UnitPrice * (1 + AllowedTolerance);
            if (current.CurrentPrice < lowerBound || current.CurrentPrice > upperBound)
                return Result.Failure(OrderErrors.PriceMismatch(item.VariantId, current.CurrentPrice, item.UnitPrice));
        }

        return Result.Success();
    }
}
