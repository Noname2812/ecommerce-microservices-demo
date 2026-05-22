using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Persistence.Constants;

namespace UrbanX.Inventory.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable(
            TableNames.InventoryItems,
            t => t.HasCheckConstraint(
                "chk_inventory_non_negative",
                "quantity_on_hand >= 0 AND quantity_reserved >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Cross-service denormalized data — no FK
        builder.Property(x => x.ProductName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.VariantSku).HasMaxLength(100).IsRequired();
        builder.Property(x => x.VariantName).HasMaxLength(255);
        builder.Property(x => x.IconUrl).HasMaxLength(1000).IsRequired();

        builder.Property(x => x.QuantityOnHand).IsRequired();
        builder.Property(x => x.QuantityReserved).IsRequired();
        builder.Property(x => x.QuantityAvailable)
            .HasComputedColumnSql("quantity_on_hand - quantity_reserved", stored: true);

        builder.HasIndex(x => x.VariantId);
    }
}
