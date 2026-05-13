using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Payment.Domain.ValueObjects;
using UrbanX.Payment.Persistence.Constants;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<PaymentEntity>
{
    public void Configure(EntityTypeBuilder<PaymentEntity> builder)
    {
        builder.ToTable(TableNames.Payments);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CustomerEmail).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProviderName).HasMaxLength(100).IsRequired();

        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.PaidAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.RemainingAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue(PaymentCurrency.Vnd).IsRequired();

        builder.Property(x => x.ProviderTransactionId).HasMaxLength(255);
        builder.Property(x => x.ProviderResponse).HasColumnType("jsonb");
        builder.Property(x => x.PaymentMethodDetails).HasColumnType("jsonb");

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .HasDefaultValue(PaymentStatus.Pending)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey).HasMaxLength(255).IsRequired();

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });

        builder.Property(x => x.ExpiresAt);
        builder.HasIndex(x => x.ProviderTransactionId);

        builder.HasOne(x => x.Provider)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
