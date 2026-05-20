using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Inventory.Application.Messaging;

/// <summary>
/// Validates <see cref="InventoryReleaseRequestedConsumerOptions"/> at startup (nested retry ranges, queue name, throughput).
/// </summary>
internal sealed class InventoryReleaseRequestedConsumerOptionsValidator : IValidateOptions<InventoryReleaseRequestedConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, InventoryReleaseRequestedConsumerOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        if (options.Retry is not null)
        {
            Validator.TryValidateObject(
                options.Retry,
                new ValidationContext(options.Retry),
                results,
                validateAllProperties: true);
        }

        if (options.QueueName is not null &&
            options.QueueName.Length > 0 &&
            string.IsNullOrWhiteSpace(options.QueueName))
        {
            results.Add(new ValidationResult(
                "QueueName cannot be whitespace-only.",
                [nameof(InventoryReleaseRequestedConsumerOptions.QueueName)]));
        }

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
        {
            results.Add(new ValidationResult(
                "PrefetchCount must be omitted or between 1 and ushort.MaxValue.",
                [nameof(InventoryReleaseRequestedConsumerOptions.PrefetchCount)]));
        }

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
        {
            results.Add(new ValidationResult(
                "ConcurrentMessageLimit must be omitted or greater than 0.",
                [nameof(InventoryReleaseRequestedConsumerOptions.ConcurrentMessageLimit)]));
        }

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
