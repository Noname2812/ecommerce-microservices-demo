using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;
using OrderEntity = UrbanX.Order.Domain.Models.Order;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : OutboxDbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
