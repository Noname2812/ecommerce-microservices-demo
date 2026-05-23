using Microsoft.Extensions.Options;

namespace UrbanX.Payment.Application.Configuration;

public sealed class MomoOptionsValidator : IValidateOptions<MomoOptions>
{
    public ValidateOptionsResult Validate(string? name, MomoOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.PartnerCode))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.PartnerCode)} is required.");

        if (string.IsNullOrWhiteSpace(options.AccessKey))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.AccessKey)} is required.");

        if (string.IsNullOrWhiteSpace(options.SecretKey))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.SecretKey)} is required.");

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.Endpoint)} must be an absolute URL.");

        if (!Uri.TryCreate(options.IpnUrl, UriKind.Absolute, out _))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.IpnUrl)} must be an absolute URL.");

        if (!Uri.TryCreate(options.RedirectUrl, UriKind.Absolute, out _))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.RedirectUrl)} must be an absolute URL.");

        if (options.Lang is not ("vi" or "en"))
            failures.Add($"{MomoOptions.SectionName}.{nameof(MomoOptions.Lang)} must be 'vi' or 'en'.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
