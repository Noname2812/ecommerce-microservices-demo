namespace UrbanX.Promotion.Application.Messaging;

/// <summary>
/// Adds Promotion-specific transient classification while delegating default transient rules to the caller.
/// This avoids duplicating (and drifting from) Shared.Messaging base defaults.
/// </summary>
internal static class CouponReleaseRequestedTransientClassification
{
    internal static bool IsTransient(Exception ex, Func<Exception, bool> defaultClassifier) =>
        ex is CouponReleaseCommandFailedException || defaultClassifier(ex);
}
