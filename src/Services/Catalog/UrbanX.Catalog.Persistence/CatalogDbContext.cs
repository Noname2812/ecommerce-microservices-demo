using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Domain.ReadModels;

namespace UrbanX.Catalog.Persistence
{
    public sealed class CatalogDbContext(
        DbContextOptions<CatalogDbContext> options) : OutboxDbContext(options)
    {
        // Write schema (public)
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Brand> Brands => Set<Brand>();
        public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
        public DbSet<VariantAttributeValue> VariantAttributeValues => Set<VariantAttributeValue>();
        public DbSet<ProductImage> ProductImages => Set<ProductImage>();
        public DbSet<VariantPriceHistory> VariantPriceHistories => Set<VariantPriceHistory>();
        public DbSet<VariantSkuHistory> VariantSkuHistories => Set<VariantSkuHistory>();

        // Read schema
        public DbSet<ProductListView> ProductListViews => Set<ProductListView>();
        public DbSet<ProductDetailView> ProductDetailViews => Set<ProductDetailView>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.HasPostgresExtension("unaccent");
            builder.HasPostgresExtension("pg_trgm");
            builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
        }
    }
}
