using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Messaging.Saga;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.Persistence.Constants;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class PlaceSalesOrderSagaStateConfiguration
    : SagaStateEfCoreConfiguration<PlaceSalesOrderSagaState>
{
    protected override string TableName => TableNames.PlaceSalesOrderSagas;

    protected override void ConfigureSaga(EntityTypeBuilder<PlaceSalesOrderSagaState> builder)
    {
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CouponCode).HasMaxLength(64);

        builder.Property(x => x.ExpectedTotal).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Subtotal).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ShippingFee).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.SaleDiscount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.CouponDiscount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.OriginalPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.FinalTotal).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.SaleStartAt);
        builder.Property(x => x.SaleEndAt);

        builder.Property(x => x.ItemsJson).HasColumnType("jsonb");
        builder.Property(x => x.VariantsJson).HasColumnType("jsonb");
        builder.Property(x => x.ShippingAddressJson).HasColumnType("jsonb");

        builder.Property(x => x.CustomerEmail).HasMaxLength(320);
        builder.Property(x => x.CustomerName).HasMaxLength(255);
        builder.Property(x => x.CustomerPhone).HasMaxLength(32);
        builder.Property(x => x.CustomerNote).HasMaxLength(1000);

        builder.Property(x => x.PaymentSessionId).HasMaxLength(255);
        builder.Property(x => x.PaymentUrl).HasMaxLength(2048);
        builder.Property(x => x.QrCodeUrl).HasMaxLength(2048);
        builder.Property(x => x.PaymentExpiresAt);

        builder.Property(x => x.OrderPersisted).IsRequired();
        builder.Property(x => x.CouponLocked).IsRequired();

        builder.Property(x => x.ValidationError).HasMaxLength(128);
        builder.Property(x => x.FailureStep).HasMaxLength(64);
        builder.Property(x => x.FailureReason).HasMaxLength(512);

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.CurrentState, x.CreatedAt });
    }
}
