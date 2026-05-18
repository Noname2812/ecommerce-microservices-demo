using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Services.Inventory.UnitTests.Domain;

public class InventoryItemConfirmDeductionTests
{
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2026-05-18T12:00:00Z");

    [Fact]
    public void ConfirmDeduction_DecrementsReservedAndOnHand()
    {
        var item = new InventoryItem
        {
            QuantityOnHand = 100,
            QuantityReserved = 10,
            UpdatedAt = Utc.AddMinutes(-1)
        };

        item.ConfirmDeduction(4, Utc);

        Assert.Equal(6, item.QuantityReserved);
        Assert.Equal(96, item.QuantityOnHand);
        Assert.Equal(Utc, item.UpdatedAt);
    }

    [Fact]
    public void ConfirmDeduction_WhenQuantityExceedsReserved_ThrowsDomainException()
    {
        var item = new InventoryItem
        {
            QuantityOnHand = 100,
            QuantityReserved = 3
        };

        Assert.Throws<InventoryDomainException>(() => item.ConfirmDeduction(5, Utc));
    }
}
