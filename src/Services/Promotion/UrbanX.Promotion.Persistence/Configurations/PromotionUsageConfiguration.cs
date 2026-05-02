using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Persistence.Constants;

namespace UrbanX.Promotion.Persistence.Configurations;

internal sealed class PromotionUsageConfiguration : IEntityTypeConfiguration<PromotionUsage>
{
    public void Configure(EntityTypeBuilder<PromotionUsage> builder)
    {
        builder.ToTable(TableNames.PromotionUsages);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.PromotionId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();

        // Idempotency: prevent double redemption on retry
        builder.HasIndex(x => new { x.PromotionId, x.OrderId }).IsUnique();
    }
}
