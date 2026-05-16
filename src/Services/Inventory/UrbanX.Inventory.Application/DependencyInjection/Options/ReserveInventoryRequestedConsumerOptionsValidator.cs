using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Inventory.Application.DependencyInjection.Options;

internal sealed class ReserveInventoryRequestedConsumerOptionsValidator
    : IValidateOptions<ReserveInventoryRequestedConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, ReserveInventoryRequestedConsumerOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        if (options.Retry is not null)
            Validator.TryValidateObject(options.Retry, new ValidationContext(options.Retry), results, validateAllProperties: true);

        if (options.Retry is { } retry && retry.MaxIntervalMs < retry.MinIntervalMs)
        {
            results.Add(new ValidationResult(
                "MaxIntervalMs must be >= MinIntervalMs.",
                [nameof(ReserveInventoryRequestedRetryOptions.MaxIntervalMs)]));
        }

        if (options.QueueName is not null && options.QueueName.Length > 0 && string.IsNullOrWhiteSpace(options.QueueName))
        {
            results.Add(new ValidationResult(
                "QueueName cannot be whitespace-only.",
                [nameof(ReserveInventoryRequestedConsumerOptions.QueueName)]));
        }

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
        {
            results.Add(new ValidationResult(
                "PrefetchCount must be omitted or between 1 and ushort.MaxValue.",
                [nameof(ReserveInventoryRequestedConsumerOptions.PrefetchCount)]));
        }

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
        {
            results.Add(new ValidationResult(
                "ConcurrentMessageLimit must be omitted or greater than 0.",
                [nameof(ReserveInventoryRequestedConsumerOptions.ConcurrentMessageLimit)]));
        }

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
