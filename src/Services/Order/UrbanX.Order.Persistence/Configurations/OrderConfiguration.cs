using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Persistence.Constants;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable(TableNames.Orders);

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(50);
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        builder.Property(o => o.CustomerId).IsRequired();
        builder.HasIndex(o => o.CustomerId);

        builder.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(255);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(255);
        builder.Property(o => o.CustomerPhone).HasMaxLength(20);

        builder.OwnsOne(o => o.ShippingAddress, addr =>
        {
            addr.Property(a => a.Street).IsRequired().HasMaxLength(255).HasColumnName("shipping_street");
            addr.Property(a => a.Ward).HasMaxLength(100).HasColumnName("shipping_ward");
            addr.Property(a => a.District).IsRequired().HasMaxLength(100).HasColumnName("shipping_district");
            addr.Property(a => a.City).IsRequired().HasMaxLength(100).HasColumnName("shipping_city");
            addr.Property(a => a.Province).HasMaxLength(100).HasColumnName("shipping_province");
            addr.Property(a => a.Country).IsRequired().HasMaxLength(100).HasColumnName("shipping_country");
            addr.Property(a => a.ZipCode).HasMaxLength(20).HasColumnName("shipping_zip_code");
            addr.Property(a => a.RecipientName).IsRequired().HasMaxLength(255).HasColumnName("shipping_recipient_name");
            addr.Property(a => a.RecipientPhone).IsRequired().HasMaxLength(20).HasColumnName("shipping_recipient_phone");
        });

        builder.Property(o => o.Subtotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.ShippingFee).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.TaxAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.CouponCode).HasMaxLength(50);
        builder.Property(o => o.CouponDiscount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);

        builder.Property(o => o.Status).IsRequired().HasMaxLength(30);
        builder.HasIndex(o => o.Status);

        builder.Property(o => o.PaymentStatus).IsRequired().HasMaxLength(30);
        builder.Property(o => o.PaymentMethod).HasMaxLength(50);
        builder.Property(o => o.PaymentReference).HasMaxLength(255);
        builder.Property(o => o.ShippingMethod).HasMaxLength(100);
        builder.Property(o => o.TrackingNumber).HasMaxLength(255);

        builder.Property(o => o.CustomerNote).HasMaxLength(1000);
        builder.Property(o => o.InternalNote).HasMaxLength(1000);
        builder.Property(o => o.CancelledReason).HasMaxLength(500);

        builder.Property(o => o.IdempotencyKey).HasMaxLength(255);

        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasIndex(o => o.CreatedAt);

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
