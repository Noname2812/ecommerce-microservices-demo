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

        builder.Property(x => x.Subtotal).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ExpectedTotal).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ShippingFee).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PromotionDiscount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.CouponDiscount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.CouponCode).HasMaxLength(64);
        builder.Property(x => x.ItemsJson).HasColumnType("jsonb");
        builder.Property(x => x.ClaimedFlashSaleSlotsJson).HasColumnType("jsonb");

        builder.Property(x => x.PaymentId);
        builder.Property(x => x.PaymentSessionId).HasMaxLength(255);

        builder.Property(x => x.FailureStep).HasMaxLength(64);
        builder.Property(x => x.FailureReason).HasMaxLength(512);

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.CurrentState, x.CreatedAt });
    }
}
