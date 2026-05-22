using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Catalog.Infrastructure.DependencyInjection.Options;

internal sealed class CatalogProjectionConsumerOptionsValidator
    : IValidateOptions<CatalogProjectionConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogProjectionConsumerOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        if (options.Retry is not null)
            Validator.TryValidateObject(options.Retry, new ValidationContext(options.Retry), results, validateAllProperties: true);

        if (options.Retry is { } retry && retry.MaxIntervalMs < retry.MinIntervalMs)
        {
            results.Add(new ValidationResult(
                "MaxIntervalMs must be >= MinIntervalMs.",
                [nameof(CatalogProjectionConsumerRetryOptions.MaxIntervalMs)]));
        }

        if (options.QueueName is not null && options.QueueName.Length > 0 && string.IsNullOrWhiteSpace(options.QueueName))
        {
            results.Add(new ValidationResult(
                "QueueName cannot be whitespace-only.",
                [nameof(CatalogProjectionConsumerOptions.QueueName)]));
        }

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
        {
            results.Add(new ValidationResult(
                "PrefetchCount must be omitted or between 1 and ushort.MaxValue.",
                [nameof(CatalogProjectionConsumerOptions.PrefetchCount)]));
        }

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
        {
            results.Add(new ValidationResult(
                "ConcurrentMessageLimit must be omitted or greater than 0.",
                [nameof(CatalogProjectionConsumerOptions.ConcurrentMessageLimit)]));
        }

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
