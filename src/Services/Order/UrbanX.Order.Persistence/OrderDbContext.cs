using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.ReadModels;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : OutboxDbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<PlaceSalesOrderSagaState>  PlaceSalesOrderSagas  => Set<PlaceSalesOrderSagaState>();
    public DbSet<PlaceOrderNormalSagaState> PlaceOrderNormalSagas => Set<PlaceOrderNormalSagaState>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<CatalogSnapshot> CatalogSnapshots => Set<CatalogSnapshot>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
