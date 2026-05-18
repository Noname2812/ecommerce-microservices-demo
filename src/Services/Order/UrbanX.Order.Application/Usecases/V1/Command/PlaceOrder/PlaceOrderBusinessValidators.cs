using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public interface IShippingValidator
{
    Task<Result> ValidateAsync(PlaceOrderShippingAddressDto shippingAddress, CancellationToken cancellationToken);
}
