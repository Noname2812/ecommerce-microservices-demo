using MassTransit;
using Microsoft.EntityFrameworkCore;
using UrbanX.Inventory.Domain.Models;

namespace UrbanX.Inventory.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
