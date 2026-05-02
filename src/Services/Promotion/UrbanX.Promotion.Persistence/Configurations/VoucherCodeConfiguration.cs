using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Persistence.Constants;

namespace UrbanX.Promotion.Persistence.Configurations;

internal sealed class VoucherCodeConfiguration : IEntityTypeConfiguration<VoucherCode>
{
    public void Configure(EntityTypeBuilder<VoucherCode> builder)
    {
        builder.ToTable(TableNames.VoucherCodes);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.Status);

        builder.Property(x => x.PromotionId).IsRequired();
    }
}
