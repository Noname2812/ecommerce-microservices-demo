using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace UrbanX.Payment.Application.Configuration;

public sealed class SePayOptionsValidator : IValidateOptions<SePayOptions>
{
    private static readonly HashSet<string> AllowedTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "compact",
        "compact2",
        "qr_only",
        "print"
    };

    public ValidateOptionsResult Validate(string? name, SePayOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BankAccount))
            failures.Add($"{SePayOptions.SectionName}.{nameof(SePayOptions.BankAccount)} is required.");

        if (string.IsNullOrWhiteSpace(options.BankCode))
            failures.Add($"{SePayOptions.SectionName}.{nameof(SePayOptions.BankCode)} is required.");

        if (string.IsNullOrWhiteSpace(options.AccountHolderName))
            failures.Add($"{SePayOptions.SectionName}.{nameof(SePayOptions.AccountHolderName)} is required.");

        if (!AllowedTemplates.Contains(options.QrTemplate))
            failures.Add($"{SePayOptions.SectionName}.{nameof(SePayOptions.QrTemplate)} must be one of: compact, compact2, qr_only, print.");

        if (string.IsNullOrWhiteSpace(options.HmacSecret) && string.IsNullOrWhiteSpace(options.WebhookSecret))
            failures.Add($"{SePayOptions.SectionName}: either {nameof(SePayOptions.HmacSecret)} or {nameof(SePayOptions.WebhookSecret)} must be set.");

        if (options.PaymentExpiresAfterHours < options.PaymentExpiresAfterHoursMinimum)
            failures.Add($"{SePayOptions.SectionName}.{nameof(SePayOptions.PaymentExpiresAfterHours)} must be >= {nameof(SePayOptions.PaymentExpiresAfterHoursMinimum)}.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
