# Saga Orchestration cho PlaceOrder / PlaceSalesOrder

> **Status**: In Progress — TASK-01 ✅ TASK-02 ✅ (bao gồm Payment step) · TASK-03 ✅ · TASK-04 ✅ · TASK-05 còn lại
> **Owner**: Order service team
> **Tasks**: xem `docs/order/tasks/` để thấy chi tiết từng task giao cho member

---

## 1. Context

UrbanX hiện chạy `PlaceOrder` và `PlaceSalesOrder` theo **sync orchestration**: Order service gọi tuần tự HTTP → Promotion → Inventory → Coupon → save order → outbox event. Compensation đã làm tốt qua `PlaceOrderCompensationBehavior` + Compensation Outbox.

### Vấn đề khi flash sale 100x traffic

1. **Thread-pool starvation**: 3 sync HTTP call/order × ngàn order/giây → block thread chờ I/O.
2. **Tail latency cộng dồn**: Promotion p99 = 800ms → Order p99 ≥ 800ms.
3. **Cascading failure**: Promotion 5xx → mọi PlaceSalesOrder fail (Promotion infinite timeout, `Order.Infrastructure/.../ServiceCollectionExtensions.cs:103-108`).
4. **Redis fail-open** cho `sales-order:guard:{idempotencyKey}` ([PlaceSalesOrderCommandHandler.cs:55-58](../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs#L55-L58)) → quota burn duplicate khi Redis blip.
5. **Coupon `UsedQuota` không atomic** với Redis quota DECR → drift counter nếu SaveChanges fail.
6. **Hot key Redis** cho flash sale 1 SKU → single Redis node bottleneck.
7. **Promotion redeem trước Inventory reserve** → nếu Inventory hết hàng phải compensation Promotion (extra round-trip).

---

## 2. Mục tiêu

- **PlaceSalesOrder**: refactor sang **async saga orchestration** (MassTransit `SagaStateMachine`), API trả `202 Accepted` ngay, client poll status. Hot path tách khỏi sync HTTP.
- **PlaceOrder (normal)**: giữ sync flow, thêm Polly hardening + observability + fix atomic Coupon. Không breaking client.
- **Hot path optimizations** cho sales: prewarm Redis quota, gateway rate limit, optional hot-key sharding.

---

## 3. Foundation đã sẵn (không cần build lại)

- `SagaStateBase`, `SagaStateMachineBase<T>`, `SagaStateEfCoreConfiguration<T>` ([src/Shared/Shared.Messaging/Saga/](../../src/Shared/Shared.Messaging/Saga/))
- `IdempotencyPipelineBehavior` (dual-check + Redis circuit breaker)
- `DistributedLockPipelineBehavior` + `[DistributedLock]` attribute
- `IOutboxWriter` + `OutboxRelayWorker`, `CompensationOutboxWriter` + `CompensationOutboxRelayWorker`
- `AddMessaging(configureBus: ...)` đã hỗ trợ saga registration
- `OrderDbContext` đã inherit `OutboxDbContext` — chỉ cần thêm `DbSet<PlaceSalesOrderSagaState>`
- Compensation events: `InventoryReleaseRequestedV1`, `CouponReleaseRequestedV1`, `FlashSaleSlotReleaseRequestedV1`, `SaleQuotaReleaseRequestedV1`
- Inventory: optimistic lock PostgreSQL `xmin` + idempotency theo `OrderIdempotencyKey`
- Promotion FlashSale slot: Redis Lua + atomic SQL UPDATE
- Polly đã có cho Inventory/Coupon HTTP (2x retry, 5s timeout); Promotion dùng `AddStandardResilienceHandler()` nhưng timeout infinite (cần fix)

---

## 4. Architecture (sau khi áp dụng)

```
Client                 Order.API           Order.Saga           Promotion       Inventory      Promotion (coupon)
  │                       │                    │                    │              │                 │
  │ POST /api/v1/orders/sales                  │                    │              │                 │
  ├──────────────────────►│                    │                    │              │                 │
  │                       │ idempotency-guard (Redis, fail-closed)  │              │                 │
  │                       │ quota-gate (Redis Lua) — burn slot      │              │                 │
  │                       │ validators (parallel)                   │              │                 │
  │                       │ Save Order(Pending) + outbox            │              │                 │
  │                       │   PlaceSalesOrderRequestedV1            │              │                 │
  │ 202 Accepted          │                                         │              │                 │
  │ { orderId, statusUrl }│                                         │              │                 │
  │◄──────────────────────┤                                         │              │                 │
                          │ ┌── OutboxRelayWorker publishes ──┐     │              │                 │
                          │                                         │              │                 │
                          │    Saga consumes Trigger ──┐            │              │                 │
                          │                            │ Promotion redeem          │                 │
                          │                            │ Inventory reserve         │                 │
                          │                            │ Coupon claim (if any)     │                 │
                          │                            │ Payment process           │                 │
                          │                            │ Publish PlaceSalesOrderConfirmedV1 (PaymentId)│
                          │                            │ (failure → compensation events)             │

Client GET /api/v1/orders/sales/{orderId}/status  → trả về current state
```

**Sync handler chỉ làm:**
1. Idempotency guard (Redis) — **fail-closed**
2. Quota gate (Redis Lua) — burn slot atomic
3. Validators cheap (product/shipping/pricing) — parallel
4. Save Order(Pending) + Publish trigger event qua **outbox transaction**

Mọi HTTP call ra service khác chuyển thành **message-based** qua RabbitMQ.

---

## 5. State machine

```
Initial → PromotionRedeeming → InventoryReserving → [CouponClaiming →] PaymentProcessing → Confirming → Confirmed (Final)
                                       ↓ fail (any state)
                                  Compensating → Failed (Final)
```

| State | Khi nhận event | Hành động | Chuyển sang |
|---|---|---|---|
| `Initial` | `PlaceSalesOrderRequestedV1` | Snapshot; Publish `RedeemSalePromotionRequestedV1` | `PromotionRedeeming` |
| `PromotionRedeeming` | `PromotionRedeemedV1` | Lưu discounts + flash-sale slots; Publish `ReserveInventoryRequestedV1` | `InventoryReserving` |
| `PromotionRedeeming` | `PromotionRedeemFailedV1` / timeout | Set FailureStep; Publish `SaleQuotaReleaseRequestedV1` | `Compensating` |
| `InventoryReserving` | `InventoryReservedV1` | Lưu `ReservationId`; nếu coupon → `ClaimCouponRequestedV1`, không coupon → `ProcessPaymentRequestedV1` | `CouponClaiming` / `PaymentProcessing` |
| `InventoryReserving` | `InventoryReserveFailedV1` / timeout | Release quota (if any) | `Compensating` |
| `CouponClaiming` | `CouponClaimedV1` | Lưu `ClaimId` + `CouponDiscount`; Publish `ProcessPaymentRequestedV1` | `PaymentProcessing` |
| `CouponClaiming` | `CouponClaimFailedV1` / timeout | Release inventory + quota | `Compensating` |
| `PaymentProcessing` | `PaymentProcessedV1` | Lưu `PaymentId`; Publish `PlaceSalesOrderConfirmedV1` (với `PaymentId`) | `Confirming` |
| `PaymentProcessing` | `PaymentProcessFailedV1` / timeout | Release inventory + coupon (if any) + quota (if any) | `Compensating` |
| `Confirming` | (enter) | `Finalize()` — xóa saga khỏi DB | `Confirmed` (removed) |
| `Compensating` | (enter) | Publish `PlaceSalesOrderFailedV1` | `Faulted` |

Mỗi state có **timeout 30s** — không nhận response → fault + compensate.

### Contracts hiện có (`Shared.Contract/Messaging/PlaceOrderSaga/`)

| File | Events |
|---|---|
| `PlaceSalesOrderRequestedV1.cs` | Trigger event — Order API → Saga |
| `PromotionEvents.cs` | `RedeemSalePromotionRequestedV1`, `PromotionRedeemedV1`, `PromotionRedeemFailedV1` |
| `InventoryEvents.cs` | `ReserveInventoryRequestedV1`, `InventoryReservedV1`, `InventoryReserveFailedV1` |
| `CouponEvents.cs` | `ClaimCouponRequestedV1`, `CouponClaimedV1`, `CouponClaimFailedV1` |
| `PaymentEvents.cs` | `ProcessPaymentRequestedV1`, `PaymentProcessedV1`, `PaymentProcessFailedV1` |
| `PlaceSalesOrderFailedV1.cs` | Terminal failure event — Saga → Order service |

---

## 6. Task breakdown (mini-tasks)

| ID | Tên | Effort | Depends |
|---|---|---|---|
| [TASK-01](tasks/TASK-01-saga-contract-events.md) | Saga Contract Events | 0.5d | — |
| [TASK-02](tasks/TASK-02-saga-state-machine.md) | Saga State + State Machine + Migration | 2d | TASK-01 |
| [TASK-03](tasks/TASK-03-promotion-consumers.md) | Promotion Service Consumers | 1.5d | TASK-01 |
| [TASK-04](tasks/TASK-04-inventory-consumer.md) | Inventory Service Consumer | 1d | TASK-01 |
| [TASK-05](tasks/TASK-05-sales-api-refactor.md) | PlaceSalesOrder API Refactor | 2d | TASK-02, 03, 04 |
| [TASK-06](tasks/TASK-06-hot-path-optimizations.md) | Hot-path Optimizations | 1.5d | — |
| [TASK-07](tasks/TASK-07-normal-hardening.md) | PlaceOrder Normal Hardening | 1d | — |
| [TASK-08](tasks/TASK-08-documentation.md) | Documentation | 1d | all |

```
TASK-01
   ├── TASK-02 ──┐
   ├── TASK-03 ──┼── TASK-05 ── TASK-08
   └── TASK-04 ──┘                ▲
                                  │
TASK-06 ─────────────────────────┤
TASK-07 ─────────────────────────┘
```

**Critical path**: TASK-01 → TASK-02 → TASK-05 → TASK-08 (~5.5 ngày).
**Tổng effort**: ~10 ngày-người, calendar ~5 ngày với 4 dev.

**Phân công gợi ý:**
- Dev A: TASK-01 → TASK-02 → TASK-05
- Dev B: TASK-03 (sau TASK-01)
- Dev C: TASK-04 (sau TASK-01) → TASK-07
- Dev D: TASK-06 (parallel) → TASK-08

---

## 7. Out of scope

- Load test / chaos engineering.
- PlaceOrder (normal) refactor sang saga — giữ sync.
- Hot-key sharding chi tiết — chờ load test xác định cần.
- BFF cookie flow thay đổi — pass-through 202 Accepted nguyên trạng.

---

## 8. Verification (end-to-end)

1. **Build**: `dotnet build UrbanX.sln` — không error.
2. **Migration**: `dotnet ef migrations add AddPlaceSalesOrderSagaState` — tạo table `place_sales_order_saga_states`.
3. **Happy path**:
   - AppHost start → seed flash sale promotion.
   - POST `/api/v1/orders/sales` → `202 Accepted` + `{ orderId, statusUrl }`.
   - Poll status → `Initiated` → `PromotionRedeeming` → `InventoryReserving` → [`CouponClaiming` →] `PaymentProcessing` → `Confirmed`.
   - RabbitMQ UI: `PlaceSalesOrderConfirmedV1` published với `PaymentId` != null.
4. **Failure paths**:
   - Inventory out-of-stock → `Compensating` → `Faulted` → quota rollback.
   - Payment declined → release inventory + coupon (if claimed) + quota → `Compensating` → `Faulted`.
   - Promotion timeout 30s → fault transition + compensation.
5. **Concurrency**:
   - 100 concurrent POST cùng idempotencyKey → 1 success, 99 dedup.
   - 1000 concurrent với 100-slot promotion → đúng 100 success, 900 `SALE_QUOTA_EXHAUSTED`.
6. **Polly**: Promotion 10s sleep → PlaceOrder normal fail trong ~5s.
7. **Observability**: Aspire Dashboard có trace đầy đủ từ Order.API → Saga → Promotion/Inventory consumers → confirm.

---

## 9. Related docs

- [Order service overview](order-service.md) — service-level info
- [Place Sales Order (current)](place-sales-order.md) — sync API hiện tại (sẽ deprecate sau migration)
- [Saga — Payment Step](saga-payment-step.md) — contracts, compensation, và interface Payment service cần implement
- [Tasks](tasks/README.md) — chi tiết từng task để giao member
