using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Messaging.Saga;
using UrbanX.Order.Application.Sagas;
using UrbanX.Order.Persistence.Constants;

namespace UrbanX.Order.Persistence.Configurations;

internal sealed class PlaceOrderNormalSagaStateConfiguration
    : SagaStateEfCoreConfiguration<PlaceOrderNormalSagaState>
{
    protected override string TableName => TableNames.PlaceOrderNormalSagas;

    protected override void ConfigureSaga(EntityTypeBuilder<PlaceOrderNormalSagaState> builder)
    {
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CouponCode).HasMaxLength(64);

        builder.Property(x => x.Subtotal).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ShippingFee).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.CouponDiscount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.ItemsJson).HasColumnType("jsonb");
        builder.Property(x => x.ShippingAddressJson).HasColumnType("jsonb");
        builder.Property(x => x.PricingSnapshotJson).HasColumnType("jsonb");
        builder.Property(x => x.CustomerEmail).HasMaxLength(320);
        builder.Property(x => x.CustomerName).HasMaxLength(255);
        builder.Property(x => x.CustomerPhone).HasMaxLength(32);
        builder.Property(x => x.CustomerNote).HasMaxLength(1000);
        builder.Property(x => x.PaymentSessionId).HasMaxLength(255);
        builder.Property(x => x.PaymentUrl).HasMaxLength(2048);
        builder.Property(x => x.QrCodeUrl).HasMaxLength(2048);
        builder.Property(x => x.PaymentExpiresAt);

        builder.Property(x => x.VariantsJson).HasColumnType("jsonb");
        builder.Property(x => x.ValidationError).HasMaxLength(128);

        builder.Property(x => x.FailureStep).HasMaxLength(64);
        builder.Property(x => x.FailureReason).HasMaxLength(512);

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.CurrentState, x.CreatedAt });
    }
}
