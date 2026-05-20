using MassTransit;
using Microsoft.EntityFrameworkCore;
using UrbanX.Payment.Domain.Models;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<PaymentProvider> PaymentProviders => Set<PaymentProvider>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.AddInboxStateEntity();
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
