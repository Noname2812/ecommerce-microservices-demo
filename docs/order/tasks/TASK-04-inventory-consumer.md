# TASK-04 · Inventory Service Consumer

| | |
|---|---|
| **Effort** | ~1 ngày |
| **Depends on** | TASK-01 |
| **Blocks** | TASK-05 |
| **Branch** | `feat/saga/task-04-inventory-consumer` |

## Goal

Thêm `ReserveInventoryRequestedConsumer` vào Inventory service. Wrap `ReserveInventoryCommandHandler` (existing) bằng MassTransit consumer.

## Context

Inventory service hiện expose `POST /internal/v1/reservations` → `ReserveInventoryCommand` (sync HTTP). Existing handler đã có:
- Idempotency theo `OrderIdempotencyKey` (return cached reservation nếu duplicate)
- Optimistic lock PostgreSQL `xmin` + retry policy qua `IConcurrencyRetriableCommand` (3 retries)
- 15 phút TTL cho reservation

Consumer chỉ là **adapter layer**, không thay đổi business logic.

**HTTP endpoint cũ vẫn giữ** — PlaceOrder normal vẫn dùng sync HTTP. Endpoint cũng được dùng cho admin testing.

## Files

### New

- `src/Services/Inventory/UrbanX.Inventory.Application/Messaging/Consumers/ReserveInventoryRequestedConsumer.cs`

### Modified

- `src/Services/Inventory/UrbanX.Inventory.API/Program.cs` — register consumer

## Implementation

### 1. ReserveInventoryRequestedConsumer

```csharp
namespace UrbanX.Inventory.Application.Messaging.Consumers;

public sealed class ReserveInventoryRequestedConsumer(
    ISender mediator,
    IPublishEndpoint publishEndpoint,
    ILogger<ReserveInventoryRequestedConsumer> logger)
    : IntegrationEventConsumerBase<ReserveInventoryRequestedV1, ReserveInventoryRequestedConsumer>(logger)
{
    protected override async Task HandleAsync(
        ConsumeContext<ReserveInventoryRequestedV1> context,
        CancellationToken ct)
    {
        var evt = context.Message;

        var command = new ReserveInventoryCommand(
            OrderIdempotencyKey: evt.OrderIdempotencyKey,
            Items: evt.Items.Select(i => new ReserveInventoryItem(
                ProductId: i.ProductId,
                VariantId: i.VariantId,
                Quantity: i.Quantity)).ToList());

        var result = await mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await publishEndpoint.Publish(new InventoryReservedV1
            {
                OrderId = evt.OrderId,
                CorrelationId = evt.OrderId.ToString("D"),
                CausationId = context.MessageId?.ToString(),
                ReservationId = result.Value.ReservationId,
                ExpiresAt = result.Value.ExpiresAt,
                Items = result.Value.Items.Select(i => new ReservedItemDto(
                    i.ProductId, i.VariantId, i.Quantity)).ToList()
            }, ct);
        }
        else
        {
            var outOfStock = ExtractOutOfStockDetail(result.Error);

            await publishEndpoint.Publish(new InventoryReserveFailedV1
            {
                OrderId = evt.OrderId,
                CorrelationId = evt.OrderId.ToString("D"),
                CausationId = context.MessageId?.ToString(),
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
                OutOfStockProducts = outOfStock
            }, ct);
        }
    }

    private static IReadOnlyList<OutOfStockDto> ExtractOutOfStockDetail(Error error)
    {
        // Parse error metadata nếu có, hoặc return empty list
        // Existing ReserveInventoryCommandHandler đã include detail trong Error
        return [];
    }
}
```

### 2. Program.cs registration

```csharp
builder.Services.AddMessaging(configureBus: bus =>
{
    bus.AddConsumer<ReserveInventoryRequestedConsumer>(cfg =>
    {
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 3,
            minInterval: TimeSpan.FromMilliseconds(200),
            maxInterval: TimeSpan.FromSeconds(2),
            intervalDelta: TimeSpan.FromMilliseconds(500)));
        cfg.UseConcurrentMessageLimit(16);
        cfg.PrefetchCount = 32;
    });
});
```

> **Note**: Inventory consumer cao throughput hơn Promotion (Concurrent 16, Prefetch 32) vì xmin optimistic retry là cheap operation, không hit external service.

## Implementation rules

1. **KHÔNG re-implement** reserve logic — Map event → `ReserveInventoryCommand`.
2. **Tận dụng existing idempotency**: `ReserveInventoryCommandHandler` đã check `OrderIdempotencyKey` (line:xx) — duplicate event → return same reservation. Consumer không cần thêm guard.
3. **Tận dụng existing concurrency retry**: `IConcurrencyRetriableCommand` handle xmin conflict tự động.
4. **`OrderIdempotencyKey` format**: caller (saga) đã append `:inv` suffix qua TASK-01 contract → consumer pass-through.
5. **CorrelationId**: `OrderId.ToString("D")`.
6. **OutOfStock detail**: nếu existing handler return detail trong `Error.Metadata` hoặc subtype — extract; nếu chưa có, empty list (caller có thể infer từ ErrorCode).

## Acceptance criteria

- [ ] HTTP endpoint `POST /internal/v1/reservations` **vẫn hoạt động**.
- [ ] Replay cùng event 2 lần (cùng `OrderIdempotencyKey`) → chỉ 1 reservation được tạo, response cùng `ReservationId`.
- [ ] Concurrent reserve cùng `productId` từ 10 saga khác nhau → xmin retry tự động, không lỗi nếu stock đủ; nếu stock cạn → đúng N success + (10-N) `InventoryReserveFailedV1`.
- [ ] Stock = 0 → `InventoryReserveFailedV1` với `ErrorCode = "INVENTORY_OUT_OF_STOCK"`.
- [ ] Test latency p99 < 50ms cho happy path (in-memory consumer test).

## Testing notes

```csharp
var harness = new InMemoryTestHarness();
var consumerHarness = harness.Consumer<ReserveInventoryRequestedConsumer>();

await harness.Start();
await harness.InputQueueSendEndpoint.Send(new ReserveInventoryRequestedV1
{
    OrderId = Guid.NewGuid(),
    OrderIdempotencyKey = "test-key:inv",
    Items = [new(productId, variantId, 2)]
});

Assert.True(await consumerHarness.Consumed.Any<ReserveInventoryRequestedV1>());
Assert.True(harness.Published.Select<InventoryReservedV1>().Any());
```

## Reference

- `IntegrationEventConsumerBase`: [src/Shared/Shared.Messaging/IntegrationEventConsumerBase.cs](../../../src/Shared/Shared.Messaging/IntegrationEventConsumerBase.cs)
- Existing handler: [src/Services/Inventory/UrbanX.Inventory.Application/Usecases/V1/Command/Reserve/ReserveInventoryCommandHandler.cs](../../../src/Services/Inventory/UrbanX.Inventory.Application/Usecases/V1/Command/Reserve/ReserveInventoryCommandHandler.cs)
- Inventory domain: [src/Services/Inventory/UrbanX.Inventory.Domain/Models/InventoryReservation.cs](../../../src/Services/Inventory/UrbanX.Inventory.Domain/Models/InventoryReservation.cs)
