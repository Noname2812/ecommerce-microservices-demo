using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Messaging;

namespace UrbanX.Services.Inventory.UnitTests.Messaging;

public class ConfirmInventoryCommandFailedExceptionTests
{
    [Theory]
    [InlineData("InventoryReservation.NotFound", true)]
    [InlineData("InventoryReservation.NotConfirmable", true)]
    [InlineData("InventoryReservation.InventoryLineMissing", true)]
    [InlineData("InventoryItem.InsufficientReservedForConfirm", false)]
    public void IsPermanent_ClassifiesErrorCodes(string code, bool expectedPermanent)
    {
        var ex = new ConfirmInventoryCommandFailedException(new Error(code, "msg"));

        Assert.Equal(expectedPermanent, ex.IsPermanent);
    }
}
