using System.ComponentModel.DataAnnotations;

namespace UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;

/// <summary>Maps to <c>UseMessageRetry(r =&gt; r.Interval(Intervals, TimeSpan.FromSeconds(IntervalSeconds)))</c>.</summary>
public sealed class CouponReleaseRequestedRetryOptions
{
    /// <summary>
    /// Number of retry intervals. Set to 0 (or <see cref="IntervalSeconds"/> to 0) to disable broker retries on this consumer.
    /// </summary>
    [Range(0, 100)]
    public int Intervals { get; set; } = 3;

    /// <summary>Delay between retries within each interval.</summary>
    [Range(0, 3600)]
    public int IntervalSeconds { get; set; } = 5;
}
