namespace UrbanX.Payment.Application.Configuration;

public sealed class SePayOptions
{
    public const string SectionName = "SePay";

    /// <summary>Secret configured in SePay dashboard; must match <c>Authorization: Bearer …</c> on webhooks.</summary>
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

    /// <summary>Delay before the first expiry sweep after host start.</summary>
    public int ExpirySweepInitialDelaySeconds { get; init; } = 5;

    /// <summary>Normal interval between sweeps (seconds).</summary>
    public int ExpirySweepIntervalSeconds { get; init; } = 60;

    /// <summary>Floor applied with <c>Math.Max</c> against <see cref="ExpirySweepIntervalSeconds"/> after errors or between runs.</summary>
    public int ExpirySweepMinimumIntervalSeconds { get; init; } = 10;
}
