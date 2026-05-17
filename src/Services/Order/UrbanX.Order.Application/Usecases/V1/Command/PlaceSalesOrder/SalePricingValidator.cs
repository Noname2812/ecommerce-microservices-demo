using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Abstractions.Promotion;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

internal sealed class SalePricingValidator(ISaleSnapshotCache snapshotCache)
    : ISalePricingValidator
{
    private const decimal Tolerance = 0.01m;
    private static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(5);

    public async Task<Result> ValidateAsync(
        Guid campaignId,
        PlaceOrderPricingSnapshotDto snapshot,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - snapshot.CapturedAt > MaxWindow)
            return Result.Failure(OrderErrors.SaleWindowExpired);

        var pricesResult = await snapshotCache.GetSalePricesAsync(campaignId, ct);
        if (pricesResult.IsFailure)
            return Result.Failure(pricesResult.Error);

        var salePrices = pricesResult.Value!;
        if (salePrices.Count == 0)
            return Result.Failure(OrderErrors.SalePricingUnavailable);

        foreach (var item in items)
        {
            if (!salePrices.TryGetValue(item.VariantId, out var expectedPrice))
                return Result.Failure(OrderErrors.SalePricingUnavailable);

            if (Math.Abs(item.UnitPrice - expectedPrice) > Tolerance)
                return Result.Failure(OrderErrors.PriceMismatch(item.VariantSku, expectedPrice, item.UnitPrice));
        }

        return Result.Success();
    }
}
