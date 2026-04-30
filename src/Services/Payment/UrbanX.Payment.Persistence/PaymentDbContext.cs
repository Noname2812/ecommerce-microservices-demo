using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;
using UrbanX.Payment.Domain.Models;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : OutboxDbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<PaymentProvider> PaymentProviders => Set<PaymentProvider>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
