using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Order.Infrastructure.DependencyInjection.Options;

internal sealed class ProductProjectionConsumerOptionsValidator
    : IValidateOptions<ProductProjectionConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductProjectionConsumerOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        if (options.Retry is not null)
            Validator.TryValidateObject(options.Retry, new ValidationContext(options.Retry), results, validateAllProperties: true);

        if (options.Retry is { } retry && retry.MaxIntervalMs < retry.MinIntervalMs)
        {
            results.Add(new ValidationResult(
                "MaxIntervalMs must be >= MinIntervalMs.",
                [nameof(ProductProjectionRetryOptions.MaxIntervalMs)]));
        }

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
        {
            results.Add(new ValidationResult(
                "PrefetchCount must be omitted or between 1 and ushort.MaxValue.",
                [nameof(ProductProjectionConsumerOptions.PrefetchCount)]));
        }

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
        {
            results.Add(new ValidationResult(
                "ConcurrentMessageLimit must be omitted or greater than 0.",
                [nameof(ProductProjectionConsumerOptions.ConcurrentMessageLimit)]));
        }

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
