using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using UrbanX.Catalog.Domain.ReadModels;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations;

internal sealed class ProductListViewConfiguration : IEntityTypeConfiguration<ProductListView>
{
    public void Configure(EntityTypeBuilder<ProductListView> builder)
    {
        builder.ToTable(TableNames.ProductListView, TableNames.ReadSchema);
        builder.HasKey(x => x.ProductId);
        builder.Property(x => x.ProductId).ValueGeneratedNever();
        builder.Property(x => x.SellerId).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CategoryName).HasMaxLength(255);
        builder.Property(x => x.BrandName).HasMaxLength(255);
        builder.Property(x => x.ShortDescription).HasMaxLength(500);
        builder.Property(x => x.BasePrice).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PrimaryImageUrl).HasMaxLength(500);
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ProjectionVersion).IsRequired();

        builder.Property(x => x.NameNormalized)
            .HasMaxLength(500)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(x => x.SkuNormalized)
            .HasMaxLength(100)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        // Shadow property: PostgreSQL-generated tsvector from normalized fields
        // NpgsqlTsVector maps to 'tsvector' natively — Npgsql EF handles the column type
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "to_tsvector('simple'," +
                " coalesce(name_normalized,'') || ' ' ||" +
                " coalesce(sku_normalized,'') || ' ' ||" +
                " coalesce(array_to_string(tags,' '),''))",
                stored: true);

        builder.HasIndex(x => new { x.SellerId, x.Status });
        builder.HasIndex(x => new { x.CategoryId, x.Status });
        builder.HasIndex(x => x.UpdatedAt);

        // GIN index: Tags array (supports @> operator)
        builder.HasIndex(x => x.Tags)
            .HasMethod("gin")
            .HasDatabaseName("ix_plv_tags_gin");

        // Trigram GIN: fuzzy / partial name search
        builder.HasIndex(x => x.NameNormalized)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_plv_name_normalized_trgm");

        // Trigram GIN: partial SKU search
        builder.HasIndex(x => x.SkuNormalized)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("ix_plv_sku_normalized_trgm");

        // GIN index: full-text search vector
        builder.HasIndex("SearchVector")
            .HasMethod("gin")
            .HasDatabaseName("ix_plv_search_vector_gin");
    }
}
