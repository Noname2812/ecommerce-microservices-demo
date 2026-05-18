using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.ValueObjects;
using UrbanX.Order.Persistence.Constants;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable(TableNames.Orders);

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(50);
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        builder.Property(o => o.UserId).IsRequired();

        builder.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(255);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(255);
        builder.Property(o => o.CustomerPhone).HasMaxLength(20);

        builder.Property(o => o.ShippingAddress)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<ShippingAddress>(value, (JsonSerializerOptions?)null)!);

        builder.Property(o => o.OriginalPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.SaleDiscount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.Subtotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.ShippingFee).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.TaxAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.FinalAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.PricingSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(o => o.CouponCode).HasMaxLength(50);
        builder.Property(o => o.CouponDiscount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.CouponClaimId);

        builder.Property(o => o.Status).IsRequired().HasMaxLength(30);
        builder.HasIndex(o => o.Status);

        builder.Property(o => o.PaymentStatus).IsRequired().HasMaxLength(30);
        builder.Property(o => o.PaymentMethod).HasMaxLength(50);
        builder.Property(o => o.PaymentReference).HasMaxLength(255);
        builder.Property(o => o.PaymentUrl).HasMaxLength(2048);
        builder.Property(o => o.QrCodeUrl).HasMaxLength(2048);
        builder.Property(o => o.ShippingMethod).HasMaxLength(100);
        builder.Property(o => o.TrackingNumber).HasMaxLength(255);

        builder.Property(o => o.CustomerNote).HasMaxLength(1000);
        builder.Property(o => o.InternalNote).HasMaxLength(1000);
        builder.Property(o => o.CancelledReason).HasMaxLength(500);

        builder.Property(o => o.IdempotencyKey).IsRequired().HasMaxLength(255);

        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique();

        builder.Property(o => o.OrderType)
            .HasColumnName("order_type")
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("Normal");

        builder.Property(o => o.CampaignId)
            .HasColumnName("campaign_id")
            .IsRequired(false);

        builder.HasIndex(o => o.CampaignId)
            .HasDatabaseName("IX_orders_campaign_id")
            .HasFilter("campaign_id IS NOT NULL");

        builder.HasIndex(o => o.CreatedAt);
        builder.HasIndex(o => new { o.UserId, o.CreatedAt })
            .IsDescending(false, true);

        // Explicit navigations — anonymous HasMany<OrderItem>() caused a second relationship → shadow FK OrderId1
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.StatusHistory)
            .WithOne()
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
