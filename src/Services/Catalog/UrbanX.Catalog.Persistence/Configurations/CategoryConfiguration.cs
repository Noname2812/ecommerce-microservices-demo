using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations
{
    internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.ToTable(TableNames.Categories);
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();

            builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
            builder.Property(x => x.Slug).HasMaxLength(255).IsRequired();
            builder.HasIndex(x => x.Slug).IsUnique();
            builder.Property(x => x.Description);
            builder.Property(x => x.ImageUrl).HasMaxLength(500);
            builder.Property(x => x.Path);
            builder.Property(x => x.IsActive).HasDefaultValue(true);
            builder.Property(x => x.DisplayOrder).HasDefaultValue(0);
            builder.Property(x => x.Depth).HasDefaultValue(0);
            builder.Property(x => x.CreatedAt).IsRequired();

            builder
                .HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
