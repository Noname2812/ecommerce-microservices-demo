using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations;

internal sealed class VariantSkuHistoryConfiguration : IEntityTypeConfiguration<VariantSkuHistory>
{
    public void Configure(EntityTypeBuilder<VariantSkuHistory> builder)
    {
        builder.ToTable(TableNames.VariantSkuHistory);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OldSku).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NewSku).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.VariantId);

        builder
            .HasOne(x => x.Variant)
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
