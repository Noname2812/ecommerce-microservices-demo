using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;

namespace UrbanX.Services.Promotion.UnitTests.Messaging;

public sealed class CouponReleaseRequestedTransientClassificationTests
{
    private static bool DefaultTransientClassifier(Exception ex) =>
        ex is TimeoutException or TaskCanceledException or OperationCanceledException;

    [Fact]
    public void IsTransient_TreatsCouponReleaseCommandFailedAsTransient_ForRetryPolicy()
    {
        var ex = new CouponReleaseCommandFailedException(new Error("CouponClaim.NotFound", "x"));
        Assert.True(CouponReleaseRequestedTransientClassification.IsTransient(ex, _ => false));
    }

    [Fact]
    public void IsTransient_DelegatesToDefaultClassifier_ForBaseTransientTypes()
    {
        Assert.True(CouponReleaseRequestedTransientClassification.IsTransient(new TimeoutException(), DefaultTransientClassifier));
        Assert.True(CouponReleaseRequestedTransientClassification.IsTransient(new TaskCanceledException(), DefaultTransientClassifier));
        Assert.True(CouponReleaseRequestedTransientClassification.IsTransient(new OperationCanceledException(), DefaultTransientClassifier));
    }

    [Fact]
    public void IsTransient_DoesNotTreatUnrelatedExceptionsAsTransient()
    {
        Assert.False(CouponReleaseRequestedTransientClassification.IsTransient(new InvalidOperationException("x"), DefaultTransientClassifier));
    }
}
