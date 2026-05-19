using Microsoft.Extensions.Options;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Domain.Errors;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

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
