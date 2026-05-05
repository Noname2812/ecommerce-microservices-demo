using Microsoft.EntityFrameworkCore;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;

namespace UrbanX.Inventory.Persistence.Seeding;

/// <summary>Dev/test seed: one warehouse and 10 stock lines with distinct available quantities.</summary>
public static class InventoryDataSeeder
{
    private static readonly Guid SeedWarehouseId = Guid.Parse("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA");

    public static async Task SeedIfEmptyAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.InventoryItems.AnyAsync(cancellationToken))
            return;

        var warehouse = new Warehouse
        {
            Id = SeedWarehouseId,
            Name = "Default DC",
            Code = "DEFAULT",
            Address = new WarehouseAddress("1 Seed St", null, null, "HCMC", null, "VN", "700000"),
            IsActive = true
        };
        context.Warehouses.Add(warehouse);

        var utc = DateTimeOffset.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            var n = i + 1;
            // Distinct available: quantity_on_hand - quantity_reserved
            var onHand = 100 - i;
            var reserved = i;

            var productId = Guid.Parse($"{n:X8}-0000-4000-8000-000000000001");
            var variantId = Guid.Parse($"{n:X8}-0000-4000-8000-000000000002");

            context.InventoryItems.Add(new InventoryItem
            {
                Id = Guid.Parse($"{n:X8}-0000-4000-8000-000000000003"),
                ProductId = productId,
                ProductName = $"Seed Product {n}",
                VariantId = variantId,
                VariantSku = $"SEED-SKU-{n:D2}",
                VariantName = $"Variant {n}",
                WarehouseId = warehouse.Id,
                QuantityOnHand = onHand,
                QuantityReserved = reserved,
                UpdatedAt = utc
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
