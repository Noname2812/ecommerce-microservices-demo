using Microsoft.Extensions.Options;

namespace UrbanX.Inventory.Application.DependencyInjection.Options;

internal sealed class ConfirmInventoryRequestedConsumerOptionsValidator
    : IValidateOptions<ConfirmInventoryRequestedConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, ConfirmInventoryRequestedConsumerOptions options)
    {
        var retry = options.Retry;
        if (retry.MinIntervalMs > retry.MaxIntervalMs)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ConfirmInventoryRequestedConsumerOptions.Retry)}: " +
                $"{nameof(retry.MinIntervalMs)} must be <= {nameof(retry.MaxIntervalMs)}.");
        }

        return ValidateOptionsResult.Success;
    }
}
