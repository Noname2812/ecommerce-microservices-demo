using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Command;

public interface IProductValidator
{
    Task<Result> ValidateAsync(IReadOnlyList<PlaceOrderLineDto> items, CancellationToken cancellationToken);
}

public interface IShippingValidator
{
    Task<Result> ValidateAsync(PlaceOrderShippingAddressDto shippingAddress, CancellationToken cancellationToken);
}

public interface IPricingValidator
{
    Task<Result> ValidateAsync(
        PlaceOrderPricingSnapshotDto snapshot,
        IReadOnlyList<PlaceOrderLineDto> items,
        CancellationToken cancellationToken);
}
