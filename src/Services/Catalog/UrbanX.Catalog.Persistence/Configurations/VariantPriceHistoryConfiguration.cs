using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations;

internal sealed class VariantPriceHistoryConfiguration : IEntityTypeConfiguration<VariantPriceHistory>
{
    public void Configure(EntityTypeBuilder<VariantPriceHistory> builder)
    {
        builder.ToTable(TableNames.VariantPriceHistory);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OldPrice).HasPrecision(18, 2);
        builder.Property(x => x.NewPrice).HasPrecision(18, 2);
        builder.Property(x => x.OldCompareAt).HasPrecision(18, 2);
        builder.Property(x => x.NewCompareAt).HasPrecision(18, 2);
        builder.Property(x => x.ChangedByName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.VariantId, x.CreatedAt }).HasDatabaseName("idx_price_history_variant");

        builder
            .HasOne(x => x.Variant)
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
