using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Persistence.Constants;

namespace UrbanX.Payment.Persistence.Configurations;

internal sealed class PaymentProviderConfiguration : IEntityTypeConfiguration<PaymentProvider>
{
    public void Configure(EntityTypeBuilder<PaymentProvider> builder)
    {
        builder.ToTable(TableNames.PaymentProviders);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Config).HasColumnType("jsonb");
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.SupportedCurrencies).HasColumnType("text[]");

        builder.HasIndex(x => x.IsActive);
    }
}
