using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Persistence.Constants;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable(TableNames.OrderItems);

        builder.HasKey(i => i.Id);

        builder.Property(i => i.OrderId).IsRequired();
        builder.Property(i => i.ProductId).IsRequired();
        builder.HasIndex(i => i.ProductId);

        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(500);
        builder.Property(i => i.ProductSlug).HasMaxLength(500);

        builder.Property(i => i.VariantId).IsRequired();
        builder.Property(i => i.VariantSku).IsRequired().HasMaxLength(100);
        builder.Property(i => i.VariantName).HasMaxLength(255);

        builder.Property(i => i.SellerId).IsRequired();
        builder.HasIndex(i => i.SellerId);
        builder.Property(i => i.SellerName).IsRequired().HasMaxLength(255);

        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(i => i.Quantity).IsRequired();
        builder.Property(i => i.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(i => i.Subtotal).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(i => i.ImageUrl).HasMaxLength(500);
        builder.Property(i => i.Status).IsRequired().HasMaxLength(30);
        builder.Property(i => i.RefundedQuantity).HasDefaultValue(0);
    }
}
