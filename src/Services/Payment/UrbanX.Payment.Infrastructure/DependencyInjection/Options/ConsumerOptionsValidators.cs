using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace UrbanX.Payment.Infrastructure.DependencyInjection.Options;

internal sealed class OrderCancelledConsumerOptionsValidator : IValidateOptions<OrderCancelledConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, OrderCancelledConsumerOptions options) =>
        ConsumerOptionsValidation.Validate(options, options.Retry);
}

internal sealed class CreatePaymentSessionConsumerOptionsValidator : IValidateOptions<CreatePaymentSessionConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, CreatePaymentSessionConsumerOptions options) =>
        ConsumerOptionsValidation.Validate(options, options.Retry);
}

internal static class ConsumerOptionsValidation
{
    internal static ValidateOptionsResult Validate(object options, ExponentialRetryOptions? retry)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        if (retry is null)
            results.Add(new ValidationResult("Retry options are required.", ["Retry"]));
        else
            Validator.TryValidateObject(retry, new ValidationContext(retry), results, validateAllProperties: true);

        return results.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(r => r.ErrorMessage!).Where(static m => !string.IsNullOrEmpty(m)));
    }
}
