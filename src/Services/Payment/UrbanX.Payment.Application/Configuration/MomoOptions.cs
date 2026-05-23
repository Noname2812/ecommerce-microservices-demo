using System.ComponentModel.DataAnnotations;

namespace UrbanX.Payment.Application.Configuration;

public sealed class MomoOptions
{
    public const string SectionName = "Momo";

    [Required, MaxLength(64)]
    public string PartnerCode { get; init; } = string.Empty;

    [Required, MaxLength(64)]
    public string AccessKey { get; init; } = string.Empty;

    [Required, MinLength(16), MaxLength(256)]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>MoMo gateway base URL — sandbox: https://test-payment.momo.vn ; prod: https://payment.momo.vn</summary>
    [Required, Url]
    public string Endpoint { get; init; } = "https://test-payment.momo.vn";

    /// <summary>Public URL MoMo POSTs IPN events to. Must include the webhook path segment.</summary>
    [Required, Url]
    public string IpnUrl { get; init; } = string.Empty;

    /// <summary>Where MoMo redirects the user back after payment (frontend URL).</summary>
    [Required, Url]
    public string RedirectUrl { get; init; } = string.Empty;

    /// <summary>Default language for MoMo hosted pages ("vi" or "en").</summary>
    [MaxLength(4)]
    public string Lang { get; init; } = "vi";

    /// <summary>Lifetime of a payment session (used to set Payment.ExpiresAt). MoMo create request has no explicit expiry.</summary>
    [Range(60, 24 * 3600)]
    public int RequestExpireSeconds { get; init; } = 1800;

    /// <summary>HTTP timeout when calling MoMo gateway endpoints.</summary>
    [Range(5, 120)]
    public int TimeoutSeconds { get; init; } = 30;
}
