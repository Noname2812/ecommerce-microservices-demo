using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;
using UrbanX.Payment.Persistence.Constants;

namespace UrbanX.Payment.Persistence.Configurations;

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable(TableNames.Refunds);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(255);
        builder.Property(x => x.ProviderRefundId).HasMaxLength(255);

        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .HasDefaultValue(RefundStatus.Pending)
            .IsRequired();

        builder.HasIndex(x => x.PaymentId);

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.Refunds)
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
