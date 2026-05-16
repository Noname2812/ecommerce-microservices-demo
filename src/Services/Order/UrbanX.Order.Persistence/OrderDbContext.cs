using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.Domain.Models;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : OutboxDbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<PlaceSalesOrderSagaState> PlaceSalesOrderSagas => Set<PlaceSalesOrderSagaState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
