# Order Service — Async Ticket Flow (Final Design)

> **Status:** Approved 2026-05-17 — sẵn sàng triển khai
> **Owner:** Order team (coordinate với Inventory + Shared/Platform)
> **Task breakdown:** xem [`task/README.md`](task/README.md)

## Mục đích

Refactor Order service để:

1. **Sửa đúng semantics flow đặt hàng**: PENDING_PAYMENT sau khi reserve, CONFIRMED sau khi payment, CANCELLED khi timeout 15p hoặc lỗi
2. **Handler async hoàn toàn** — return 202 ngay trong < 20ms với `ticketId`, mọi thứ còn lại do saga đảm nhận
3. **Cleanup Catalog read model snapshot** — xoá toàn bộ projection consumers, snapshot reader/writer, Redis cache; saga gọi thẳng Catalog HTTP với circuit breaker
4. **Thay custom `Shared.Outbox`** bằng MassTransit EF Outbox built-in (atomic với `SaveChanges`)
5. **Bổ sung Inventory.ConfirmReservation** — hard deduct stock thật sự sau khi payment thành công
6. **Endpoint polling** `GET /orders/ticket/{ticketId}` để client theo dõi tiến trình

## Flow chuẩn

```
Order Handler (sync, < 20ms)
 ├── FluentValidation (memory)
 ├── Check pending limit (Redis)
 ├── Publish PlaceOrderRequestedV1 { ticketId, userId, items, ... }
 └── Return 202 { ticketId }

Saga (async)
 ├── [1] Validate product/seller/price  → ICatalogServiceClient (sync HTTP + circuit breaker)
 ├── [2] Save Order { status = PROCESSING }
 ├── [3] Reserve Inventory               → soft lock
 ├── [4] Claim Coupon                    → nếu có
 ├── [5] Create Payment Session          → lưu paymentUrl, Order: PROCESSING → PENDING_PAYMENT
 └── [6] Schedule timeout 15 phút        → Saga tự quản lý (PaymentExpiry)

Client polling GET /orders/ticket/{ticketId}
 ├── PROCESSING       → đang chạy saga
 ├── PENDING_PAYMENT  → { paymentUrl } → redirect thanh toán
 ├── CONFIRMED        → done
 └── CANCELLED        → { reason }

Payment callback
 ├── PaymentCompleted
 │    ├── Hard deduct inventory (ConfirmReservation)
 │    ├── Status = CONFIRMED
 │    └── Publish OrderConfirmedV1 → email, analytics, seller...
 │
 └── PaymentTimeoutExpired (Saga schedule)
      ├── Release inventory
      ├── Release coupon
      ├── Status = CANCELLED
      └── Publish OrderCancelledV1 → notify user
```

## Order Status (mới)

| Status | Khi nào | Set bởi |
|---|---|---|
| `PROCESSING` | Saga đã validate xong, đang reserve/coupon/payment session | `Order.Create` (saga step 2) |
| `PENDING_PAYMENT` | Đã có payment URL, chờ user thanh toán (15 phút) | `Order.MarkReadyForPayment` (saga step 5) |
| `CONFIRMED` | Payment thành công | `Order.MarkPaid` (saga) |
| `CANCELLED` | Timeout / validation fail / user huỷ / inventory không đủ | `Order.Cancel` (saga compensating hoặc handler) |

Status hậu-CONFIRMED (`SHIPPED`, `DELIVERED`, `REFUND_REQUESTED`, `REFUNDED`) — giữ trong enum cho future logistics flow, **không touch** trong scope này.

## Sales Flow (Flash sale + Coupon)

Sales order **mở rộng** flow Normal với 2 cơ chế đặc thù: **Flash sale stock gate** ở handler + **Coupon lock atomic** ở saga.

```
Order Handler Sales (sync < 20ms)
 [1] FluentValidation                       → input format, ExpectedTotal > 0
 [2] Pending limit (Redis)                  → max 3 pending/user (Normal=1, Sales=3)
 [3] Flash sale gate (Redis Lua atomic)     → DECRBY flashsale:{saleId}:stock — 409 SoldOut
 → Publish PlaceSalesOrderRequestedV1
 → Return 202 { ticketId }

Saga Sales
 [1] Validate product/seller/price          → ICatalogServiceClient + server-side pricing recalc
 [2] Validate flash sale còn hiệu lực       → ISaleEligibilityService (thời gian, quota, user-bought)
 [3] Validate & lock coupon (soft claim)    → ICouponLockService Lua atomic
 [4] Save Order { status = PROCESSING } + snapshot pricing (OriginalPrice + SaleDiscount + CouponDiscount + ShippingFee + FinalTotal)
 [5] Reserve inventory (soft lock)
 [6] Create payment session
 [7] MarkReadyForPayment → PENDING_PAYMENT + Schedule 15-min timeout

Payment outcome
 ✅ PaymentCompleted
    [1] Hard deduct inventory (ConfirmInventoryRequestedV1)
    [2] Confirm coupon use (hard deduct permanent)
    [3] Status = CONFIRMED
    [4] Publish OrderConfirmedV1
 ❌ PaymentTimeoutExpired
    [1] Release inventory
    [2] Release coupon lock
    [3] Restore Redis flash sale stock (INCRBY)
    [4] Status = CANCELLED
    [5] Publish OrderCancelledV1
```

### Server-side pricing — không tin client

```
originalPrice  = catalog.price × quantity        (server tính)
saleDiscount   = flash sale rule (%, fixed)      (server tính)
couponDiscount = coupon rule (%, fixed)          (server tính)
shippingFee    = address + seller config         (server tính)
─────────────────────────────────────────────────
finalTotal     = originalPrice - saleDiscount - couponDiscount + shippingFee
```

Nếu `|expectedTotal - finalTotal| / finalTotal > 0.01` (tolerance 1%) → reject `OrderErrors.PriceMismatch` (409) + trả breakdown mới nhất cho client refresh.

### Flash sale stock — 3 thời điểm

| Thời điểm | Action |
|---|---|
| Handler [3] | `DECRBY flashsale:{saleId}:stock` (Lua atomic) — reject `409 Sold Out` nếu hết |
| Saga [2] | Validate sale còn hiệu lực (time window, user-already-bought) |
| Timeout / fail | `INCRBY flashsale:{saleId}:stock` — trả lại quota |

### Coupon — 3 thời điểm

| Thời điểm | Action |
|---|---|
| Saga [3] | Lock (Lua atomic): SISMEMBER eligible → DECR remaining → SADD locked-users → EXPIRE 16min |
| Payment success | Confirm: SREM locked-users → SADD used-users (permanent) |
| Timeout / cancel | Release: INCR remaining → SREM locked-users |

### Rollback map (per failure step)

| Bước fail | Compensate |
|---|---|
| [1] Validate product/price | Restore Redis flash sale stock |
| [2] Validate sale eligibility | Restore Redis stock |
| [3] Validate coupon lock | Restore Redis stock |
| [4] Save order | Release coupon lock + Restore Redis stock |
| [5] Reserve inventory | Release coupon lock + Restore Redis stock |
| [6] Payment session | Release inventory + Release coupon + Restore Redis stock |
| [7] Payment timeout 15p | Release inventory + Release coupon + Restore Redis stock |

### Cross-service Redis keys (owned by Promotion)

Promotion service phải seed Redis khi campaign/coupon create:
- `flashsale:{saleId}:stock = quotaTotal` (string int)
- `coupon:{code}:remaining = totalQuota` (string int)
- `coupon:{code}:eligible-users` (set) — hoặc rule-based nếu user pool lớn
- `coupon:{code}:locked-users` (set, ephemeral)
- `coupon:{code}:used-users` (set, permanent)
- `coupon:{code}:meta-discount` (string)
- `coupon:{code}:meta-discount-type` (`"FIXED"` | `"PERCENT"`)

Coordinate trong Promotion team task riêng (out of scope của Order refactor).

### Normal vs Sales — khác biệt summary

| Feature | Normal | Sales |
|---|---|---|
| Handler pending limit | 1/user | 3/user |
| Handler Flash sale gate | ❌ | ✅ (Redis Lua DECRBY) |
| Saga validate sale eligibility | ❌ | ✅ |
| Coupon: lock + confirm + release | Event-based (Promotion service) | Redis Lua atomic |
| Pricing tolerance check | ❌ (giữ snapshot 30p) | ✅ 1% server-recalc |
| Server flow steps | 5 | 7 |
| Compensation complexity | Standard | + Restore flash stock |

## Polling response

```
GET /api/v1/order/orders/ticket/{ticketId}

200 OK
{
  "ticketId": "...",
  "status":   "PROCESSING | PENDING_PAYMENT | CONFIRMED | CANCELLED",
  "orderId":  "..." | null,
  "paymentUrl": "...",                          // chỉ có khi PENDING_PAYMENT
  "qrCodeUrl":  "...",
  "paymentStatus": "Unpaid | AwaitingPayment | Paid",
  "cancelledReason": "...",                     // chỉ có khi CANCELLED
  "paymentExpiresAt": "2026-05-17T16:30:00Z"   // chỉ có khi PENDING_PAYMENT
}
```

**Trạng thái lookup:**
1. Query Order theo `Id = ticketId` (vì `ticketId == orderId` sau khi saga save)
2. Nếu Order chưa exist → query saga state theo `CorrelationId = ticketId`:
   - Saga active → `PROCESSING`
   - Saga Faulted → `CANCELLED` + `FailureReason`
   - Cả 2 không thấy → 404

## Race condition strategy

| Scenario | Mitigation |
|---|---|
| PaymentExpiry vs PaymentCompleted (saga) | `ConcurrencyMode.Optimistic` — 1 thắng, kia retry → state khác → no-op |
| MarkPaid sau khi Cancel commit | Domain guard `if (Status == Cancelled) return;` |
| PaymentSessionCreated sau khi Faulted | `Status != Processing` → MarkReadyForPayment no-op |
| ConfirmInventory + Cancel race | ConfirmReservation idempotent check `Status == Confirmed` → skip |
| User race PlaceOrder 2 ticket | Redis pending slot — atomic Lua INCR + EXPIRE |
| Catalog service down | Circuit breaker (Polly) → `Catalog.Unavailable` → saga Faulted + slot release |

## Idempotency strategy

Phân tán = duplicate event là chuyện thường: MassTransit redelivery, MT EF Outbox publish retry, RabbitMQ at-least-once, saga retry trên concurrency conflict, user double-click. Mỗi nơi xử lý event đều phải idempotent.

### Infrastructure layer

| Cơ chế | Áp dụng |
|---|---|
| **MT EF Outbox `DuplicateDetectionWindow=10min`** | MT tự skip publish duplicate (cùng `MessageId`) |
| **MT Inbox table** | Consumer auto-dedupe (cùng `MessageId`) |
| **Saga `CorrelateById(OrderId)`** | 1 event chỉ match 1 saga instance |
| **HttpIdempotency middleware** | Order.API `AddHttpIdempotency` — request-level cache theo header `Idempotency-Key` |
| **RabbitMQ at-least-once + MT retry** | Per-consumer retry exponential max 3 lần |

### Domain layer (guards)

| Method | Guard |
|---|---|
| `Order.MarkReadyForPayment` | `if (Status != Processing) return;` |
| `Order.MarkPaid` | (a) `Status == Cancelled` → return; (b) `Status == Confirmed && PaymentStatus == Paid` → return; (c) `Status != PendingPayment` → throw |
| `Order.Cancel` | `if (Status == Cancelled) return;` |
| `InventoryReservation.Confirm` | `if (Status == Confirmed) return;` |
| `InventoryItem.ConfirmDeduction` | Wrapped trong cùng TX với `Confirm` (atomic) |
| `InventoryReservation.MarkReleased` | `if (Status == Released) return;` (đã có) |

### Application layer (handlers)

| Handler | Idempotency strategy |
|---|---|
| `PlaceOrderCommandHandler` | (a) HttpIdempotency middleware; (b) `cmd.IdempotencyKey` publish vào event — saga dùng `{key}:inv`, `{key}:cpn`, `{key}:pay` cho sub-keys; (c) Redis pending slot |
| `CancelOrderCommandHandler` | Domain `Cancel` idempotent; MT outbox dedupe `MessageId` |
| `ConfirmReservationCommandHandler` | `IConcurrencyRetriableCommand` + check `Reservation.Status == Confirmed` → skip |
| `ReleaseReservationCommandHandler` | Đã có; idempotent nếu `Status == Released` |
| `ReserveInventoryCommandHandler` | Dedupe theo `OrderIdempotencyKey` (`{key}:inv`) — trả existing ReservationId |

### Saga state transitions

- **Duplicate Initially trigger**: saga đã exist → `When(Requested)` không match Initially filter, MT log warning, skip
- **InventoryReserved 2 lần**: lần 1 transition CouponClaiming/PaymentSessionCreating; lần 2 không match During(InventoryReserving), skip
- **Concurrent retry**: MT auto-retry 3 lần với optimistic concurrency; fail → vào `_error` queue
- **`Schedule(...)` duplicate fire**: MT scheduler dùng `tokenId`; `Unschedule` set null; nếu fire trước Unschedule kịp → state check no-op

### Redis pending slot Lua scripts

**`TryAcquire`** (atomic):
```lua
local current = redis.call('INCR', KEYS[1])
if current == 1 then
    redis.call('EXPIRE', KEYS[1], ARGV[1])    -- TTL only on first increment
end
if current > tonumber(ARGV[2]) then
    redis.call('DECR', KEYS[1])               -- rollback nếu vượt limit
    return 0
end
return current
```

**`Release`** (atomic, không underflow):
```lua
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
if current <= 0 then return 0 end
return redis.call('DECR', KEYS[1])
```
→ Release gọi 2 lần (saga compensating + handler cancel) chỉ decrement 1 lần thật sự.

### Deterministic MessageId cho saga publish

Saga set explicit `MessageId` = deterministic hash từ `OrderId + eventType` để dedup hoạt động qua saga retry:
```csharp
await publisher.Publish<OrderConfirmedV1>(msg, ctx => {
    ctx.MessageId = DeterministicGuid($"order-confirmed:{order.Id}");
});
```

### Test checklist idempotency

- [ ] POST PlaceOrder cùng `Idempotency-Key` 2 lần → cùng `ticketId`, chỉ 1 saga + 1 Order
- [ ] Mock publish `PaymentSessionCompletedV1` 2 lần → Order CONFIRMED 1 lần, Inventory deduct 1 lần, `OrderConfirmedV1` publish 1 lần
- [ ] Mock publish `PaymentExpiry` + `PaymentCompleted` đồng thời → exactly 1 outcome
- [ ] Cancel order đã CANCELLED → no-op
- [ ] Inventory `ConfirmReservationCommand` 2 lần cùng `ReservationId` → `QuantityOnHand` giảm 1 lần
- [ ] Redis pending slot: TryAcquire → Release → Release → slot=0 (không âm)
- [ ] Saga thrash retry (force concurrency conflict) → eventual consistency, không duplicate side-effect

## Decisions ghi nhận

| # | Decision | Rationale |
|---|---|---|
| 1 | Saga tạo Order (handler chỉ publish ticket) | Async ≤ 20ms; lo lắng validate/save làm chậm response loại bỏ |
| 2 | Polling endpoint thay vì SSE/WebSocket | Đơn giản, ít infra, đủ cho UX 15 phút |
| 3 | Inventory thêm ConfirmReservation (hard deduct) | Đúng semantic Reserve → Deduct; track stock thực bán |
| 4 | Pending limit qua appsettings (`MaxPendingPerUser=1`, `PendingSlotTtlMinutes=30`) | Tuân thủ rule "No Magic Values" |
| 5 | MT EF Outbox thay Shared.Outbox cho cả Normal + Sales saga | Built-in, atomic SaveChanges, giảm boilerplate |
| 6 | Xoá toàn bộ Catalog snapshot system trong Order | "Read model thừa" — saga gọi HTTP với CB là đủ |
| 7 | Polly resilience cho `ICatalogServiceClient` | CB + retry chống Catalog flap khi peak |
| 8 | Publish `OrderConfirmedV1`/`OrderCancelledV1` | Cho downstream services (email, analytics, seller) subscribe sau |

## Configuration mới

### `appsettings.json` (Order.API)
```json
{
  "PlaceOrder": {
    "MaxNormalPendingPerUser": 1,
    "MaxSalesPendingPerUser": 3,
    "PendingSlotTtlMinutes": 30,
    "CouponLockTtlSeconds": 960,
    "PriceMismatchTolerance": 0.01
  },
  "OrderPayment": {
    "NormalOrderExpiryMinutes": 15,
    "SalesOrderExpiryMinutes": 15
  }
}
```

### Polly resilience (`Order.Infrastructure/ServiceCollectionExtensions.cs`)
```csharp
builder.Services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>(...)
    .AddStandardResilienceHandler(o => {
        o.CircuitBreaker.SamplingDuration   = TimeSpan.FromSeconds(30);
        o.CircuitBreaker.FailureRatio       = 0.5;
        o.CircuitBreaker.MinimumThroughput  = 10;
        o.Retry.MaxRetryAttempts             = 2;
        o.AttemptTimeout.Timeout             = TimeSpan.FromSeconds(3);
        o.TotalRequestTimeout.Timeout        = TimeSpan.FromSeconds(10);
    });
```

## Out of Scope

- KHÔNG đổi `Shared.Outbox` ở Catalog/Identity/Payment service
- KHÔNG refactor Payment service flow nội bộ
- KHÔNG refactor Promotion service (saga vẫn publish `RedeemSalePromotionRequestedV1` cho Sales)
- KHÔNG migrate data từ bảng cũ (dev project, drop được)
- Downstream consumers của `OrderConfirmedV1`/`OrderCancelledV1` — out of scope, các team email/analytics/seller subscribe sau

## Related Docs

- [`task/README.md`](task/README.md) — chi tiết các task để các team implement
- [`reserve-vs-deduct.md`](reserve-vs-deduct.md) — semantic Reserve vs Hard Deduct ở Inventory (sẽ tạo sau)
- [`../auth/trust-gateway-flow.md`](../auth/trust-gateway-flow.md) — Trust-the-Gateway auth pattern
- [`../shared/shared-cache.md`](../shared/shared-cache.md) — Redis cache + distributed lock
