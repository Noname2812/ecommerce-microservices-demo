using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations
{
    internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
    {
        public void Configure(EntityTypeBuilder<Brand> builder)
        {
            builder.ToTable(TableNames.Brands);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();

            builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
            builder.HasIndex(x => x.Name).IsUnique();
            builder.Property(x => x.Slug).HasMaxLength(255).IsRequired();
            builder.HasIndex(x => x.Slug).IsUnique();
            builder.Property(x => x.LogoUrl).HasMaxLength(500);
            builder.Property(x => x.IsActive).HasDefaultValue(true);
        }
    }
}
