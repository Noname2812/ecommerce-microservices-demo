using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Persistence.Constants;

namespace UrbanX.Promotion.Persistence.Configurations;

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable(TableNames.Coupons);

        // Id (from BaseEntity<string>) stores the coupon code — column named "code"
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired()
            .ValueGeneratedNever();

        // Code is a computed alias for Id — not mapped as a separate column
        builder.Ignore(x => x.Code);

        builder.Property(x => x.DiscountType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DiscountValue).HasPrecision(18, 2);
        builder.Property(x => x.MinOrderValue).HasPrecision(18, 2);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.IsActive).HasDatabaseName("ix_coupons_is_active");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_coupons_expires_at");
    }
}
