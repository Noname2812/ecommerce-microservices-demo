using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Catalog.Domain.Models;
using UrbanX.Catalog.Persistence.Constants;

namespace UrbanX.Catalog.Persistence.Configurations
{
    internal sealed class VariantAttributeValueConfiguration : IEntityTypeConfiguration<VariantAttributeValue>
    {
        public void Configure(EntityTypeBuilder<VariantAttributeValue> builder)
        {
            builder.ToTable(TableNames.VariantAttributeValues);
            builder.HasKey(x => new { x.VariantId, x.AttributeId });
            builder.Property(x => x.Value).HasMaxLength(255).IsRequired();

            builder
                .HasOne(x => x.Variant)
                .WithMany(x => x.AttributeValues)
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(x => x.AttributeDefinition)
                .WithMany()
                .HasForeignKey(x => x.AttributeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
