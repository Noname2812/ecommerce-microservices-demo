using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;
using UrbanX.Payment.Persistence.Constants;

namespace UrbanX.Payment.Persistence.Configurations;

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable(TableNames.PaymentEvents);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb");

        builder.Property(x => x.Source)
            .HasMaxLength(50)
            .HasDefaultValue(EventSource.Internal)
            .IsRequired();

        builder.Property(x => x.ExternalTransactionId).HasMaxLength(PaymentEventConstraints.ExternalTransactionIdMaxLength);
        builder.Property(x => x.TransferAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(x => x.ExternalTransactionId)
            .IsUnique()
            .HasDatabaseName("idx_payment_event_ext_tx_id")
            .HasFilter("external_transaction_id IS NOT NULL");

        builder.HasIndex(x => new { x.PaymentId, x.CreatedAt });

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
