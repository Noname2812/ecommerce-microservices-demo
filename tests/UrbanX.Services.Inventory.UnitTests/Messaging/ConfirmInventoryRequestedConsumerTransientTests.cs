using Shared.Kernel.Exceptions;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Messaging;

namespace UrbanX.Services.Inventory.UnitTests.Messaging;

public class ConfirmInventoryRequestedConsumerTransientTests
{
    private static bool Classify(Exception ex) =>
        ConfirmInventoryTransientClassifier.IsTransient(ex, _ => false);

    [Fact]
    public void IsTransient_WhenPermanentConfirmFailure_ReturnsFalse()
    {
        var ex = new ConfirmInventoryCommandFailedException(
            new Error("InventoryReservation.NotFound", "missing"));

        Assert.False(Classify(ex));
    }

    [Fact]
    public void IsTransient_WhenConcurrencyRetryExhausted_ReturnsTrue()
    {
        var ex = new ConcurrencyRetryExhaustedException(
            "exhausted",
            new InvalidOperationException("xmin conflict"));

        Assert.True(Classify(ex));
    }

    [Fact]
    public void IsTransient_WhenTimeout_DelegatesToDefaultClassifier()
    {
        Assert.True(ConfirmInventoryTransientClassifier.IsTransient(
            new TimeoutException(),
            ex => ex is TimeoutException));
    }
}
