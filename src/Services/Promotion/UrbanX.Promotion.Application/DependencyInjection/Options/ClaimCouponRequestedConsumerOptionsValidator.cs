using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Promotion.Application.DependencyInjection.Options;

internal sealed class ClaimCouponRequestedConsumerOptionsValidator
    : IValidateOptions<ClaimCouponRequestedConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, ClaimCouponRequestedConsumerOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        if (options.Retry is null)
        {
            results.Add(new ValidationResult(
                "Retry options are required.",
                [nameof(ClaimCouponRequestedConsumerOptions.Retry)]));
        }
        else
        {
            Validator.TryValidateObject(options.Retry, new ValidationContext(options.Retry), results, validateAllProperties: true);
        }

        if (!string.IsNullOrEmpty(options.QueueName) && string.IsNullOrWhiteSpace(options.QueueName))
            results.Add(new ValidationResult("QueueName cannot be whitespace-only.", [nameof(ClaimCouponRequestedConsumerOptions.QueueName)]));

        if (options.PrefetchCount is { } prefetch && prefetch == 0)
            results.Add(new ValidationResult("PrefetchCount must be omitted or between 1 and ushort.MaxValue.", [nameof(ClaimCouponRequestedConsumerOptions.PrefetchCount)]));

        if (options.ConcurrentMessageLimit is not null && options.ConcurrentMessageLimit <= 0)
            results.Add(new ValidationResult("ConcurrentMessageLimit must be omitted or greater than 0.", [nameof(ClaimCouponRequestedConsumerOptions.ConcurrentMessageLimit)]));

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
