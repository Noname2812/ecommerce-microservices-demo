using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Persistence.Constants;

namespace UrbanX.Inventory.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable(TableNames.StockMovements);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.MovementType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.QuantityChange).IsRequired();
        builder.Property(x => x.QuantityBefore).IsRequired();
        builder.Property(x => x.QuantityAfter).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(50);
        builder.Property(x => x.CreatedByName).HasMaxLength(255);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.InventoryItemId, x.CreatedAt });

        builder.HasOne(x => x.InventoryItem)
            .WithMany(x => x.Movements)
            .HasForeignKey(x => x.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
