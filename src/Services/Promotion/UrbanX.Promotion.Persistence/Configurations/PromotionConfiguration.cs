using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Persistence.Constants;

namespace UrbanX.Promotion.Persistence.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Domain.Models.Promotion>
{
    public void Configure(EntityTypeBuilder<Domain.Models.Promotion> builder)
    {
        builder.ToTable(TableNames.Promotions);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Type).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DiscountType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DiscountValue).HasPrecision(18, 2);
        builder.Property(x => x.MaxDiscountCap).HasPrecision(18, 2);
        builder.Property(x => x.MinOrderAmount).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TargetScope).HasMaxLength(30).IsRequired();
        builder.Property(x => x.TargetIds).HasColumnType("uuid[]");

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => x.StartsAt);
        builder.HasIndex(x => x.EndsAt);

        builder.HasMany(e => e.Codes)
            .WithOne()
            .HasForeignKey(c => c.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Codes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.FlashSaleItems)
            .WithOne()
            .HasForeignKey(f => f.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.FlashSaleItems)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
