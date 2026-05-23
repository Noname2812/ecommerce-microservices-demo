using System.ComponentModel.DataAnnotations;

namespace UrbanX.Payment.Application.Configuration;

public sealed class SePayOptions
{
    public const string SectionName = "SePay";

    /// <summary>Bank account number receiving inbound transfers (shown on VietQR + matched on webhook).</summary>
    [Required, MaxLength(50)]
    public string BankAccount { get; init; } = string.Empty;

    /// <summary>VietQR bank code (e.g. "MB", "VCB", "TPB", "TCB", "VIB"). Must be supported by qr.sepay.vn.</summary>
    [Required, MaxLength(50)]
    public string BankCode { get; init; } = string.Empty;

    /// <summary>Display name for the receiving account holder (shown to user but not signed in URL).</summary>
    [Required, MaxLength(200)]
    public string AccountHolderName { get; init; } = string.Empty;

    /// <summary>VietQR template: compact (default), compact2, qr_only, print.</summary>
    [MaxLength(20)]
    public string QrTemplate { get; init; } = "compact";

    /// <summary>HMAC-SHA256 shared secret used to verify webhook signatures. Empty disables HMAC (legacy bearer fallback).</summary>
    [Required, MinLength(16), MaxLength(256)]
    public string HmacSecret { get; init; } = string.Empty;

    /// <summary>Acceptable clock skew between webhook timestamp header and server clock (seconds).</summary>
    [Range(60, 600)]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    /// <summary>How long the QR code / payment session is valid after CreatePaymentSession (in minutes).</summary>
    [Range(1, 1440)]
    public int PaymentSessionExpiresAfterMinutes { get; init; } = 30;

    /// <summary>Secret configured in SePay dashboard; only honoured when <see cref="HmacSecret"/> is empty (legacy bearer auth).</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>Hours until a bank-transfer payment is treated as expired if not fully paid.</summary>
    public int PaymentExpiresAfterHours { get; init; } = 72;

    /// <summary>If <see cref="PaymentExpiresAfterHours"/> is below this, <see cref="PaymentExpiresAfterHoursFallback"/> is used instead.</summary>
    public int PaymentExpiresAfterHoursMinimum { get; init; } = 1;

    /// <summary>Used when configured expiry hours are invalid (below minimum).</summary>
    public int PaymentExpiresAfterHoursFallback { get; init; } = 72;

    /// <summary>Max rows returned from memo ILike narrow query before regex disambiguation.</summary>
    public int WebhookMemoMatchCandidateLimit { get; init; } = 50;

    /// <summary>Max payments processed per expiry sweep iteration.</summary>
    public int ExpirySweepBatchSize { get; init; } = 200;
}
