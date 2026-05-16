# Reserve Inventory Consumer

## Purpose

`ReserveInventoryRequestedConsumer` consumes `ReserveInventoryRequestedV1` events published by the Order saga (`PlaceOrderSaga`). It delegates to `ReserveInventoryCommandHandler` (existing) and publishes the outcome back to the saga.

## Event flow

```
PlaceOrderSaga
  → publish ReserveInventoryRequestedV1
      → ReserveInventoryRequestedConsumer
          → ReserveInventoryCommand (MediatR)
              → success → publish InventoryReservedV1
              → failure → publish InventoryReserveFailedV1
```

## Consumed event

`Shared.Contract.Messaging.PlaceOrderSaga.ReserveInventoryRequestedV1`

| Field | Description |
|---|---|
| `OrderId` | Saga correlation ID |
| `OrderIdempotencyKey` | Must end in `:inv` suffix (set by saga) |
| `Items` | `IReadOnlyList<InventoryReserveItem(ProductId, VariantId, Quantity)>` |

## Published events

- `InventoryReservedV1` — reservation created or idempotent replay returned same reservation
- `InventoryReserveFailedV1` — out of stock or product not found; `OutOfStockProducts` populated when error is `OutOfStockError`

Both events carry `CorrelationId = OrderId.ToString("D")` and `CausationId = incoming EventId`.

## Idempotency

Handled by `ReserveInventoryCommandHandler` via `OrderIdempotencyKey`. Duplicate messages return the same `ReservationId` without creating a new reservation.

## Configuration

Section: `Inventory:Messaging:ReserveInventoryRequested`

| Key | Default | Description |
|---|---|---|
| `QueueName` | MassTransit default | Override receive endpoint queue name |
| `Retry.RetryLimit` | `3` | Number of exponential retry attempts |
| `Retry.MinIntervalMs` | `200` | Minimum retry interval (ms) |
| `Retry.MaxIntervalMs` | `2000` | Maximum retry interval (ms) |
| `Retry.IntervalDeltaMs` | `500` | Exponential delta (ms) |
| `PrefetchCount` | `32` | RabbitMQ QoS prefetch |
| `ConcurrentMessageLimit` | `16` | Parallel message processing limit |

Higher throughput than the compensation consumer (`InventoryReleaseRequested`) because xmin optimistic retry is a cheap in-process operation with no external service calls.
