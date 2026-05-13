using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Application.Clients;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

internal sealed class SalePricingValidator(IPromotionServiceClient promotionClient)
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

        var lines = items
            .Select(i => new PromotionSalePriceLine(i.VariantId, i.UnitPrice))
            .ToList();
        var salePricesResult = await promotionClient.GetSalePricesAsync(campaignId, lines, ct);

        if (salePricesResult.IsFailure)
            return Result.Failure(OrderErrors.SalePricingUnavailable);

        var salePrices = salePricesResult.Value!;

        var distinctVariants = items.Select(i => i.VariantId).Distinct().Count();
        if (distinctVariants > 0 && salePrices.Count == 0)
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
