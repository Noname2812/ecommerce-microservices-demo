using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;

internal sealed class CouponReleaseRequestedConsumerOptionsValidator : IValidateOptions<CouponReleaseRequestedConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, CouponReleaseRequestedConsumerOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        if (options.Retry is null)
        {
            results.Add(new ValidationResult(
                "Retry options are required.",
                [nameof(CouponReleaseRequestedConsumerOptions.Retry)]));
        }
        else
        {
            Validator.TryValidateObject(
                options.Retry,
                new ValidationContext(options.Retry),
                results,
                validateAllProperties: true);
        }

        // Non-empty after trim would still be empty (e.g. "   ") — invalid queue name.
        if (!string.IsNullOrEmpty(options.QueueName) && string.IsNullOrWhiteSpace(options.QueueName))
        {
            results.Add(new ValidationResult(
                "QueueName cannot be whitespace-only.",
                [nameof(CouponReleaseRequestedConsumerOptions.QueueName)]));
        }

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
        {
            results.Add(new ValidationResult(
                "PrefetchCount must be omitted or between 1 and ushort.MaxValue.",
                [nameof(CouponReleaseRequestedConsumerOptions.PrefetchCount)]));
        }

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
        {
            results.Add(new ValidationResult(
                "ConcurrentMessageLimit must be omitted or greater than 0.",
                [nameof(CouponReleaseRequestedConsumerOptions.ConcurrentMessageLimit)]));
        }

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
