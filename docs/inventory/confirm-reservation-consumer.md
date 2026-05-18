# Confirm Reservation Consumer

## Purpose

`ConfirmInventoryRequestedConsumer` consumes `ConfirmInventoryRequestedV1` events published by the Order saga when payment succeeds. It hard-deducts stock: transitions a reservation from **PENDING** (soft hold) to **CONFIRMED** and decreases `QuantityOnHand` on the linked `InventoryItem`.

This is fire-and-forget from the saga’s perspective — the saga does not wait for an `InventoryConfirmedV1` reply in v1.

## Event flow

```
PlaceOrderNormalSaga (PaymentCompleted)
  → publish ConfirmInventoryRequestedV1
      → ConfirmInventoryRequestedConsumer
          → ConfirmInventoryRequestedProcessor
              → ConfirmReservationCommand (MediatR)
                  → success (idempotent if already CONFIRMED)
                  → failure → ConfirmInventoryCommandFailedException
```

## Consumed event

`Shared.Contract.Messaging.PlaceOrderSaga.ConfirmInventoryRequestedV1`

| Field | Description |
|---|---|
| `OrderId` | Saga correlation ID |
| `ReservationId` | Head reservation row to confirm (from `InventoryReservedV1`) |
| `IdempotencyKey` | `{orderIdempotencyKey}:confirm-inv` (saga shard) |

## Idempotency

1. **Inbox** — when `EventId` is present, `processed_events` is checked first (`ExistsAsync`); successful outcomes call `StageInsert` with `EventType = IConfirmInventoryRequested` (same transaction as stock changes).
2. **Status check** — if reservation is already `CONFIRMED`, handler returns success without creating another `StockMovement` (still stages inbox when `EventId` is set).
3. **Row lock** — `GetTrackedByIdWithInventoryItemForUpdateAsync` uses `SELECT … FOR UPDATE` on `inventory_reservations` inside the MediatR transaction.
4. **Optimistic concurrency** — `InventoryItem` uses PostgreSQL `xmin`; concurrent updates to the same line retry via `IConcurrencyRetriableCommand` + `ExecuteInTransactionWithConcurrencyRetryAsync`.

Permanent failures (`NotConfirmable`, etc.) do **not** stage inbox so poison messages can be investigated.

## Permanent failures (no broker retry)

These error codes are classified as **permanent** in `ConfirmInventoryCommandFailedException.IsPermanent`:

| Error code | Meaning |
|---|---|
| `InventoryReservation.NotFound` | Unknown `ReservationId` |
| `InventoryReservation.NotConfirmable` | Status is not `PENDING` (e.g. already `RELEASED`) |
| `InventoryReservation.InventoryLineMissing` | Reservation has no linked `InventoryItem` |

MassTransit `UseMessageRetry` calls `Ignore<ConfirmInventoryCommandFailedException>(ex => ex.IsPermanent)` so these go straight to the error queue without exponential retries.

Transient failures still retry per endpoint policy, including:

- DB timeouts / cancellations (`IntegrationEventConsumerBase` defaults)
- `ConcurrencyRetryExhaustedException` when in-process `xmin` retries (3×) are exhausted — broker exponential retry may succeed after contention clears

Permanent command failures are excluded from broker retry via `Ignore<ConfirmInventoryCommandFailedException>(ex => ex.IsPermanent)`.

Classification logic lives in `ConfirmInventoryTransientClassifier` (internal, unit-tested without reflection).

## Audit trail

`StockMovement` rows use:

- `MovementType` = `SALE`
- `Note` = `ORDER_CONFIRMED`
- `CreatedByName` = `system:saga-confirm` (no gateway user context on this path)

## Configuration

Section: `Inventory:Messaging:ConfirmInventoryRequested`

| Key | Default | Description |
|---|---|---|
| `QueueName` | `inventory-i-confirm-inventory-requested` | Receive endpoint queue name |
| `Retry.RetryLimit` | `3` | Exponential retry attempts (non-permanent errors only) |
| `Retry.MinIntervalMs` | `200` | Minimum retry interval (ms) |
| `Retry.MaxIntervalMs` | `2000` | Maximum retry interval (ms) |
| `Retry.IntervalDeltaMs` | `500` | Exponential delta (ms) |
| `PrefetchCount` | `32` | RabbitMQ QoS prefetch |
| `ConcurrentMessageLimit` | `16` | Parallel message processing limit |

## Related

- Reserve flow: [reserve-inventory-consumer.md](./reserve-inventory-consumer.md)
- Release/compensation: `InventoryReleaseRequestedConsumer` on `compensation.events` fanout
