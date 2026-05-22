using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Domain.ValueObjects;
using UrbanX.Inventory.Persistence.Constants;

namespace UrbanX.Inventory.Persistence.Configurations;

internal sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable(TableNames.InventoryReservations);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status).HasMaxLength(20).HasDefaultValue(ReservationStatus.Pending).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ReleasedAt);
        builder.Property(x => x.ConfirmedAt).IsRequired(false);

        builder.HasIndex(x => x.OrderId);

        builder.HasIndex(x => new { x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_inventory_reservations_status_expires_at");

        builder.HasOne(x => x.InventoryItem)
            .WithMany(x => x.Reservations)
            .HasForeignKey(x => x.InventoryItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
