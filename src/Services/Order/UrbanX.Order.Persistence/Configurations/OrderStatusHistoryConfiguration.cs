using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Persistence.Constants;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable(TableNames.OrderStatusHistories);

        builder.HasKey(h => h.Id);

        builder.Property(h => h.OrderId).IsRequired();
        builder.Property(h => h.FromStatus).HasMaxLength(30);
        builder.Property(h => h.ToStatus).IsRequired().HasMaxLength(30);
        builder.Property(h => h.Note).HasMaxLength(500);
        builder.Property(h => h.ChangedByName).HasMaxLength(255);
    }
}
