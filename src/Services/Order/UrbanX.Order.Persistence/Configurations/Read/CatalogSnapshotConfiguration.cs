using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Order.Domain.ReadModels;
using UrbanX.Order.Persistence.Constants;

namespace UrbanX.Order.Persistence.Configurations.Read;

internal sealed class CatalogSnapshotConfiguration : IEntityTypeConfiguration<CatalogSnapshot>
{
    public void Configure(EntityTypeBuilder<CatalogSnapshot> builder)
    {
        builder.ToTable(TableNames.CatalogSnapshots, TableNames.ReadSchema);

        builder.HasKey(x => x.VariantId);
        builder.Property(x => x.VariantId).ValueGeneratedNever();

        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.Sku).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ProductIsActive).IsRequired();
        builder.Property(x => x.VariantIsActive).IsRequired();
        builder.Property(x => x.CurrentPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.ProjectionVersion).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.ProductId);
    }
}
