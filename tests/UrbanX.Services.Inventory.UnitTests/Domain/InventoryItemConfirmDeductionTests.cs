using UrbanX.Inventory.Domain.Errors;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Services.Inventory.UnitTests.Domain;

public sealed class InventoryItemConfirmDeductionTests
{
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2026-05-18T12:00:00Z");

    private static InventoryItem CreateItem(int onHand, int reserved) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Test",
            VariantId = Guid.NewGuid(),
            VariantSku = "SKU-1",
            IconUrl = "https://example.com/icon.png",
            QuantityOnHand = onHand,
            QuantityReserved = reserved,
        };

    [Fact]
    public void ConfirmDeduction_DecrementsReservedAndOnHand()
    {
        var item = CreateItem(100, 10);

        var error = item.ConfirmDeduction(4, Utc);

        Assert.Null(error);
        Assert.Equal(6, item.QuantityReserved);
        Assert.Equal(96, item.QuantityOnHand);
    }

    [Fact]
    public void ConfirmDeduction_WhenQuantityExceedsReserved_ReturnsError()
    {
        var item = CreateItem(100, 3);

        var error = item.ConfirmDeduction(5, Utc);

        Assert.NotNull(error);
    }

    [Fact]
    public void ReleaseReservedQuantity_WhenInsufficientReserved_ReturnsError()
    {
        var item = CreateItem(100, 2);

        var error = item.ReleaseReservedQuantity(5);

        Assert.Equal(InventoryStockErrors.InsufficientReservedForRelease(item.Id, 5, 2).Code, error!.Code);
    }
}
