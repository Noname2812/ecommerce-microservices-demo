# TASK-03 · Promotion Service Consumers

| | |
|---|---|
| **Effort** | ~1.5 ngày |
| **Depends on** | TASK-01 |
| **Blocks** | TASK-05 |
| **Branch** | `feat/saga/task-03-promotion-consumers` |

## Goal

Thêm 2 MassTransit consumers vào Promotion service để xử lý request events từ Order saga, **tái sử dụng** existing MediatR commands — không re-implement business logic.

## Context

Hiện tại Promotion service expose:
- `POST /api/v1/promotions/redeem` → `RedeemPromotionCommand` (sync HTTP)
- `POST /api/v1/coupons/claim` → coupon claim command (sync HTTP)

Sau migration, Order saga publish event `RedeemSalePromotionRequestedV1` / `ClaimCouponRequestedV1` qua RabbitMQ. Consumer của Promotion catch, map sang existing MediatR command, publish response event.

**HTTP endpoints cũ vẫn giữ** — PlaceOrder normal vẫn dùng sync HTTP.

## Files

### New

1. `src/Services/Promotion/UrbanX.Promotion.Application/Messaging/Consumers/RedeemSalePromotionRequestedConsumer.cs`
2. `src/Services/Promotion/UrbanX.Promotion.Application/Messaging/Consumers/ClaimCouponRequestedConsumer.cs`

### Modified

- `src/Services/Promotion/UrbanX.Promotion.API/Program.cs` — register consumers + per-endpoint tuning

## Implementation

### 1. RedeemSalePromotionRequestedConsumer

```csharp
namespace UrbanX.Promotion.Application.Messaging.Consumers;

public sealed class RedeemSalePromotionRequestedConsumer(
    ISender mediator,
    IPublishEndpoint publishEndpoint,
    ILogger<RedeemSalePromotionRequestedConsumer> logger)
    : IntegrationEventConsumerBase<RedeemSalePromotionRequestedV1, RedeemSalePromotionRequestedConsumer>(logger)
{
    protected override async Task HandleAsync(
        ConsumeContext<RedeemSalePromotionRequestedV1> context,
        CancellationToken ct)
    {
        var evt = context.Message;

        // Map event → existing command
        var command = new RedeemPromotionCommand(
            CustomerId: evt.UserId,
            OrderId: evt.OrderId,
            Subtotal: evt.Subtotal,
            CouponCode: evt.CouponCode,
            Items: evt.Items.Select(i => new RedeemPromotionItem(
                ProductId: i.ProductId,
                VariantId: i.VariantId,
                UnitPrice: i.UnitPrice,
                Quantity: i.Quantity)).ToList());

        var result = await mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await publishEndpoint.Publish(new PromotionRedeemedV1
            {
                OrderId = evt.OrderId,
                CorrelationId = evt.OrderId.ToString("D"),
                CausationId = context.MessageId?.ToString(),
                OrderLevelDiscount = result.Value.OrderLevelDiscount,
                ItemDiscounts = result.Value.ItemDiscounts,
                AppliedPromotionIds = result.Value.AppliedPromotionIds,
                ClaimedFlashSaleSlots = result.Value.ClaimedFlashSaleSlots.Select(s =>
                    new ClaimedSlotDto(s.PromotionId, s.SlotKey, s.Quantity)).ToList()
            }, ct);
        }
        else
        {
            await publishEndpoint.Publish(new PromotionRedeemFailedV1
            {
                OrderId = evt.OrderId,
                CorrelationId = evt.OrderId.ToString("D"),
                CausationId = context.MessageId?.ToString(),
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message
            }, ct);
        }
    }
}
```

### 2. ClaimCouponRequestedConsumer

Pattern tương tự — map sang `ClaimCouponCommand` (existing).

### 3. Program.cs registration

```csharp
builder.Services.AddMessaging(configureBus: bus =>
{
    bus.AddConsumer<RedeemSalePromotionRequestedConsumer>(cfg =>
    {
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 3,
            minInterval: TimeSpan.FromMilliseconds(200),
            maxInterval: TimeSpan.FromSeconds(2),
            intervalDelta: TimeSpan.FromMilliseconds(500)));
        cfg.UseConcurrentMessageLimit(8);
        cfg.PrefetchCount = 16;
    });

    bus.AddConsumer<ClaimCouponRequestedConsumer>(cfg =>
    {
        cfg.UseMessageRetry(r => r.Exponential(3, /* ... */));
        cfg.UseConcurrentMessageLimit(8);
        cfg.PrefetchCount = 16;
    });
});
```

## Implementation rules

1. **KHÔNG re-implement** logic redeem/claim — chỉ map event → command qua MediatR.
2. **Idempotency**: existing `RedeemPromotionCommand` đã có idempotency theo `OrderId`. Consumer không cần thêm guard.
3. **CorrelationId convention**: luôn set `CorrelationId = OrderId.ToString("D")` trên response event để saga match.
4. **CausationId**: set bằng `context.MessageId` để trace ngược.
5. **Error mapping**: lấy `result.Error.Code` từ `Result<T>.Error`. Không expose stack trace ra event.
6. **Transient exception**: cho phép `IntegrationEventConsumerBase` re-throw (existing behavior) để MassTransit retry policy xử lý.
7. **DLQ**: sau retry exhausted, message vào dead-letter queue. Saga sẽ timeout 30s và transition Compensating độc lập.

## Acceptance criteria

- [ ] HTTP endpoint cũ `POST /api/v1/promotions/redeem` **vẫn hoạt động** (PlaceOrder normal regression).
- [ ] Test integration: publish `RedeemSalePromotionRequestedV1` từ test → consumer xử lý → `PromotionRedeemedV1` xuất hiện trên bus.
- [ ] Test failure: redeem fail (vd campaign expired) → `PromotionRedeemFailedV1` published, không throw.
- [ ] Test concurrency: 50 concurrent events khác OrderId → tất cả processed, không deadlock.
- [ ] Idempotency: replay cùng event 2 lần → chỉ 1 promotion usage record được tạo (existing logic handles).

## Testing notes

Sử dụng `MassTransit.Testing` với `IntegrationEventConsumerBase` test harness:

```csharp
var harness = new InMemoryTestHarness();
var consumerHarness = harness.Consumer<RedeemSalePromotionRequestedConsumer>(/* DI factory */);
await harness.Start();

await harness.InputQueueSendEndpoint.Send(new RedeemSalePromotionRequestedV1 { /* ... */ });

Assert.True(harness.Published.Select<PromotionRedeemedV1>().Any());
```

## Reference

- `IntegrationEventConsumerBase`: [src/Shared/Shared.Messaging/IntegrationEventConsumerBase.cs](../../../src/Shared/Shared.Messaging/IntegrationEventConsumerBase.cs)
- Existing handler: [src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/RedeemPromotion/RedeemPromotionCommandHandler.cs](../../../src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/RedeemPromotion/RedeemPromotionCommandHandler.cs)
- AddMessaging extension: [src/Shared/Shared.Messaging/DependencyInjection/Extensions/ServiceCollectionExtensions.cs](../../../src/Shared/Shared.Messaging/DependencyInjection/Extensions/ServiceCollectionExtensions.cs)
