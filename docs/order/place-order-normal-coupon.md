# PlaceOrderNormal saga — coupon flow (Post-Phase 3)

Mô tả luồng coupon **mới** trong `PlaceOrderNormalSagaStateMachine` sau khi refactor Phase 3: coupon được hold ở Cart, saga chỉ verify token + áp dụng discount; DB claim chạy off critical path sau payment success.

> Sales flow giữ nguyên (dùng [RedisCouponLockService](../../src/Services/Order/UrbanX.Order.Infrastructure/Services/RedisCouponLockService.cs) lock lúc validate). Doc này chỉ áp dụng cho Normal.

## Flow tổng quan

```
Cart screen:
  POST /api/v1/promotion/coupon-holds  (Promotion: Redis-only Lua, no DB write)
    → HoldToken (TTL 15m) + DiscountAmount + DiscountType

Checkout:
  POST /api/v1/order/orders/place  body { CouponHoldToken, ... }
    → publish PlaceOrderRequestedV1 { CouponHoldToken, ... }

Saga state machine (Normal):

  Initial
    ├── SnapshotRequest (copy CouponHoldToken vào saga state)
    ├── ValidateThroughCatalogAsync (HTTP Catalog, cached 30s)
    ├── ResolveCouponHoldAsync                ⬅ Phase 3 new
    │     ├── 1 Redis GET coupon:hold:{token}
    │     ├── If null → ValidationError = "COUPON_HOLD_EXPIRED"
    │     ├── If hold.UserId != saga.UserId → "COUPON_HOLD_USER_MISMATCH"
    │     ├── Populate saga.CouponCode + saga.CouponDiscount
    │     └── order.ApplyCoupon(code, discount)  (in-place DB pricing update)
    ├── If ValidationError → Faulted (cancel + release pending slot)
    └── Else → Schedule InventoryTimeout, Publish ReserveInventoryRequestedV1 → InventoryReserving

  InventoryReserving
    ├── On InventoryReservedV1 → Schedule PaymentSessionTimeout
    │                          → Publish CreatePaymentSessionV1
    │                          → PaymentSessionCreating
    ├── On InventoryReserveFailedV1 / InventoryTimeout → Compensating

  PaymentSessionCreating
    ├── On PaymentSessionCreatedV1 → MarkReadyForPaymentAsync
    │     ├── Success → Schedule PaymentExpiry → PaymentPending
    │     └── Failure → publish InventoryRelease + ReleaseCouponHoldIfAnyAsync + cancel → Faulted
    ├── On PaymentSessionTimeout → publish InventoryRelease + ReleaseCouponHoldIfAnyAsync → Compensating

  PaymentPending
    ├── On PaymentSessionCompletedV1
    │     ├── Unschedule PaymentExpiry
    │     ├── MarkOrderPaidAsync
    │     ├── Publish ConfirmInventoryRequestedV1
    │     ├── If saga.CouponHoldToken != null:
    │     │     Publish ClaimCouponRequestedV1 { HoldToken, ... }  ⬅ Phase 3 fire-and-forget
    │     ├── PublishOrderConfirmedAsync
    │     ├── ReleasePendingSlot
    │     └── Finalize
    └── On PaymentExpiry → publish InventoryRelease + ReleaseCouponHoldIfAnyAsync + cancel → Faulted

  Compensating
    ├── If ReservationIds.Any && FailureStep != "PaymentSessionTimeout" → publish InventoryRelease
    ├── If CouponHoldToken != null && FailureStep != "PaymentSessionTimeout" → ReleaseCouponHoldIfAnyAsync
    ├── CancelOrderAsync + PublishOrderCancelledAsync + ReleasePendingSlot
    └── Faulted
```

## So với Phase 2 (luồng cũ)

| Step | Cũ | Mới |
|---|---|---|
| Cart → Order | Client gửi `CouponCode` | Client hold trước, gửi `CouponHoldToken` |
| Saga step coupon | State `CouponClaiming` (5s timeout, await `CouponClaimedV1`) | Không có state riêng — resolve hold ở Initial (1 Redis GET) |
| Promotion service call trong critical path | Có (event/saga, +1 saga roundtrip) | Không (Order đọc trực tiếp Redis của Promotion) |
| DB claim trong Promotion | Đồng bộ trong saga | Async sau `PaymentCompletedV1` (fire-and-forget, MT outbox retry nếu fail) |
| Compensation | Publish `CouponReleaseRequestedV1` (event qua Promotion) | Order tự release hold qua Lua (DEL user-lock + INCR quota) |

## Saga state changes

```csharp
public sealed class PlaceOrderNormalSagaState : SagaStateBase
{
    // Mới:
    public string? CouponHoldToken { get; set; }   // raw token từ event

    // Đổi semantics:
    public string? CouponCode { get; set; }        // resolved từ hold ở Initial step
    public decimal CouponDiscount { get; set; }    // resolved từ hold ở Initial step

    // Removed (dead code):
    // public Guid? CouponClaimId { get; set; }    — vẫn còn trong DB column cho backward compat,
    //                                                 saga Normal không set value mới
}
```

## Resilience

| Failure mode | Hành vi |
|---|---|
| HoldToken expired/invalid | Initial fail với `COUPON_HOLD_EXPIRED` → cancel order, release pending slot |
| HoldToken thuộc user khác | `COUPON_HOLD_USER_MISMATCH` → same as above |
| Redis down lúc resolve | Saga throw → MT retry/DLQ (transient); user-facing 500 |
| Payment fail/expire | `ReleaseCouponHoldIfAnyAsync` cleanup Redis (DEL token + DEL user-lock + INCR quota) |
| Post-payment claim fail (Promotion DB) | MT outbox retry; customer đã trả discounted price, claim record gap chỉ ảnh hưởng analytics |
| Cross-service Redis key format drift | Order không tìm thấy hold → `COUPON_HOLD_EXPIRED` → user-facing fail. Test integration để bắt sớm. |

## Files liên quan

| File | Thay đổi |
|---|---|
| [PlaceOrderNormalSagaStateMachine.cs](../../src/Services/Order/UrbanX.Order.Infrastructure/Sagas/PlaceOrderNormal/PlaceOrderNormalSagaStateMachine.cs) | Bỏ `CouponClaiming` state, thêm `ResolveCouponHoldAsync` + `ReleaseCouponHoldIfAnyAsync`, claim post-payment |
| [PlaceOrderNormalSagaState.cs](../../src/Services/Order/UrbanX.Order.Application/Sagas/PlaceOrderNormal/PlaceOrderNormalSagaState.cs) | Thêm `CouponHoldToken` |
| [PlaceOrderCommand.cs](../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderCommand.cs) | `CouponCode` → `CouponHoldToken` |
| [PlaceOrderRequestedV1.cs](../../src/Shared/Shared.Contract/Messaging/PlaceOrder/PlaceOrderRequestedV1.cs) | Thêm `CouponHoldToken` |
| [ICouponHoldClient.cs](../../src/Services/Order/UrbanX.Order.Application/Clients/ICouponHoldClient.cs) | Mới — read/release hold cross-service |
| [CouponHoldClient.cs](../../src/Services/Order/UrbanX.Order.Infrastructure/Services/CouponHoldClient.cs) | Mới — Lua release |
| [Order.cs](../../src/Services/Order/UrbanX.Order.Domain/Models/Order.cs) | Thêm `ApplyCoupon` method |
| [CouponEvents.cs](../../src/Shared/Shared.Contract/Messaging/PlaceOrderSaga/CouponEvents.cs) | `ClaimCouponRequestedV1` thêm `HoldToken?` |
| [ClaimCouponCommand.cs](../../src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/ClaimCoupon/ClaimCouponCommand.cs) | Thêm `HoldToken` param; handler skip Redis acquire khi token provided |

## Migration / rollout

- Cả 2 service (Order + Promotion) phải deploy đồng bộ vì:
  - `PlaceOrderRequestedV1.CouponHoldToken` field mới (Promotion cũ vẫn nhận event nhưng sẽ ignore field — backward compat)
  - `ClaimCouponRequestedV1.HoldToken` field mới (Promotion handler cần biết để skip Redis acquire)
- Frontend phải migrate trước khi flow mới hoạt động — nếu vẫn gửi `CouponCode` (old), Normal command sẽ reject (validator chỉ chấp nhận `CouponHoldToken`).
- Sales flow KHÔNG đổi. Cart hold endpoint không apply cho Sales — Sales tự lock qua saga.
