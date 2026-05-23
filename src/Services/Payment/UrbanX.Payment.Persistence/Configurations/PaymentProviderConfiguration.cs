using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;
using UrbanX.Payment.Persistence.Constants;

namespace UrbanX.Payment.Persistence.Configurations;

internal sealed class PaymentProviderConfiguration : IEntityTypeConfiguration<PaymentProvider>
{
    /// <summary>Stable seed id for the built-in SePay provider row.</summary>
    public static readonly Guid SePayProviderId = Guid.Parse("11111111-1111-1111-1111-000000000001");

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

        builder.HasData(new PaymentProvider
        {
            Id = SePayProviderId,
            Name = "SePay",
            Type = ProviderType.Sepay,
            IsActive = true,
            SupportedCurrencies = ["VND"]
        });
    }
}
