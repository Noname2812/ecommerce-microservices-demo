using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public interface ISaleEligibilityValidator
{
    Task<Result> ValidateAsync(
        Guid campaignId,
        Guid userId,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken ct);
}

public interface ISalePricingValidator
{
    Task<Result> ValidateAsync(
        Guid campaignId,
        PlaceOrderPricingSnapshotDto snapshot,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken ct);
}
