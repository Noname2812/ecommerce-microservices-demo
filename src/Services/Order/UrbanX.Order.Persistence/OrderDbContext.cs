using MassTransit;
using Microsoft.EntityFrameworkCore;
using UrbanX.Order.Application.Sagas.PlaceOrderNormal;
using UrbanX.Order.Application.Sagas.PlaceOrderSales;
using UrbanX.Order.Domain.Models;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<PlaceSalesOrderSagaState>  PlaceSalesOrderSagas  => Set<PlaceSalesOrderSagaState>();
    public DbSet<PlaceOrderNormalSagaState> PlaceOrderNormalSagas => Set<PlaceOrderNormalSagaState>();
    public DbSet<ProductVariantReadModel> ProductVariants => Set<ProductVariantReadModel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
