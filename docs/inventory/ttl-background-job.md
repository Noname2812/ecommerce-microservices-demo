# TTL Background Job — Release Expired Reservations

Releases `PENDING` reservations whose `ExpiresAt` has passed, restoring stock to available.

## Purpose

Prevents stock being held indefinitely if an order flow never completes (crash, timeout, abandoned flow). Must be running before the Reserve API is live.

## Schedule

Hangfire `RecurringJob`, cron `*/2 * * * *` — every 2 minutes.  
Job ID: `ttl-release-expired-reservations`

## Behavior

1. Queries up to **200** reservations where `Status = 'PENDING' AND ExpiresAt < UTC_NOW`, ordered by `ExpiresAt ASC` (oldest first).
2. For each reservation (inside one DB transaction):
   - `InventoryReservation.MarkReleased(utcNow)` — sets `Status = RELEASED`, `ReleasedAt = utcNow`
   - `InventoryItem.ReleaseReservedQuantity(quantity, utcNow)` — decrements `QuantityReserved`; `QuantityAvailable` is a computed column (`quantity_on_hand - quantity_reserved`) so it updates automatically
3. Logs `[ttl-job-{timestamp}] Released {N} expired reservations` after a non-empty run. Empty batch → no log (debug-level only).

## Idempotency

Two concurrent runs cannot double-release:

- **WHERE Status='PENDING'** in the query ensures a committed RELEASED row is never re-processed.
- `InventoryItem` uses **PostgreSQL xmin optimistic concurrency**. If a race still occurs, the second transaction fails with `DbUpdateConcurrencyException` and rolls back cleanly — no quantity is double-decremented.

## Storage

`Hangfire.InMemory` in development. For production, swap to `Hangfire.PostgreSql`:

```csharp
// Program.cs
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(connectionString));
```

## Key Files

| File | Role |
|---|---|
| [ReleaseExpiredReservationsJob.cs](../../src/Services/Inventory/UrbanX.Inventory.Application/Jobs/ReleaseExpiredReservationsJob.cs) | Job logic |
| [IInventoryReservationRepository.cs](../../src/Services/Inventory/UrbanX.Inventory.Domain/IInventoryReservationRepository.cs) | `GetExpiredPendingBatchAsync` |
| [Program.cs](../../src/Services/Inventory/UrbanX.Inventory.API/Program.cs) | Hangfire registration + schedule |
| [ReleaseExpiredReservationsJobTests.cs](../../tests/UrbanX.Services.Inventory.UnitTests/Jobs/ReleaseExpiredReservationsJobTests.cs) | Unit tests |
