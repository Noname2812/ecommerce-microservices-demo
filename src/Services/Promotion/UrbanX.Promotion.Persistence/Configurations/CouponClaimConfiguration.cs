using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Persistence.Constants;

namespace UrbanX.Promotion.Persistence.Configurations;

internal sealed class CouponClaimConfiguration : IEntityTypeConfiguration<CouponClaim>
{
    public void Configure(EntityTypeBuilder<CouponClaim> builder)
    {
        builder.ToTable(TableNames.CouponClaims);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CouponCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.OrderIdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();

        builder.HasOne<Coupon>()
            .WithMany()
            .HasForeignKey(x => x.CouponCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique partial index: one CLAIMED claim per (coupon, user) at a time
        builder.HasIndex(x => new { x.CouponCode, x.UserId })
            .IsUnique()
            .HasFilter("\"Status\" = 'CLAIMED'")
            .HasDatabaseName("ix_coupon_claims_code_user_claimed");

        // For TTL job: scan by (Status, ExpiresAt) to find expired claims
        builder.HasIndex(x => new { x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_coupon_claims_status_expires_at");
    }
}
