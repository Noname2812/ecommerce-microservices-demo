# Saga — Payment Step

## Mục đích

Bước `PaymentProcessing` là bước cuối cùng trong saga `PlaceSalesOrder` trước khi xác nhận đơn hàng. Saga publish `ProcessPaymentRequestedV1` tới Payment service; Payment service xử lý giao dịch rồi publish kết quả ngược lại.

Bước này đảm bảo đơn hàng chỉ được xác nhận khi **cả ba điều kiện** đều đạt: promotion đã trừ slot, kho đã reserve, và thanh toán đã thành công.

---

## Vị trí trong flow

```
... → [CouponClaiming →] PaymentProcessing → Confirming → (Finalized)
                               ↓ fail
                          Compensating (release inventory + coupon + quota)
```

`PaymentProcessing` nhận input từ:
- `CouponClaiming` nếu đơn có coupon
- `InventoryReserving` (noCoupon branch) nếu không có coupon

---

## Contracts (`Shared.Contract/Messaging/PlaceOrderSaga/PaymentEvents.cs`)

### `ProcessPaymentRequestedV1` — Saga → Payment service

| Field | Type | Ghi chú |
|---|---|---|
| `OrderId` | `Guid` | Correlation key |
| `UserId` | `string` | |
| `OrderIdempotencyKey` | `string` | `{IdempotencyKey}:pay` — dedup trên Payment service |
| `FinalAmount` | `decimal` | `Subtotal - PromotionDiscount - CouponDiscount + ShippingFee` |
| `CampaignId` | `Guid` | |
| `ReservationId` | `Guid` | Inventory reservation — Payment service có thể ghi audit |
| `CouponClaimId` | `Guid?` | Nullable — null nếu không có coupon |

### `PaymentProcessedV1` — Payment service → Saga

| Field | Type | Ghi chú |
|---|---|---|
| `OrderId` | `Guid` | Correlation key |
| `PaymentId` | `Guid` | ID giao dịch thanh toán — lưu vào saga state và `PlaceSalesOrderConfirmedV1` |
| `Amount` | `decimal` | Số tiền đã charge |
| `ProcessedAt` | `DateTimeOffset` | |

### `PaymentProcessFailedV1` — Payment service → Saga

| Field | Type | Ghi chú |
|---|---|---|
| `OrderId` | `Guid` | Correlation key |
| `ErrorMessage` | `string` | |
| `FailureCode` | `string?` | Optional — ví dụ `INSUFFICIENT_FUNDS`, `CARD_DECLINED` |

---

## Saga state

Bước payment thêm 1 field vào `PlaceSalesOrderSagaState`:

| Property | Type | Mô tả |
|---|---|---|
| `PaymentId` | `Guid?` | Set khi nhận `PaymentProcessedV1`; null nếu chưa thanh toán hoặc failed |

Migration: `AddPaymentIdToSagaState` — thêm cột `payment_id uuid nullable` vào `place_sales_order_saga_states`.

---

## Compensation khi payment fail

Khi nhận `PaymentProcessFailedV1` hoặc timeout 30s, saga publish (theo thứ tự):

1. `InventoryReleaseRequestedV1` — luôn release (reservation đã tồn tại trước payment)
2. `CouponReleaseRequestedV1` — chỉ khi `CouponClaimId != null`
3. `SaleQuotaReleaseRequestedV1` — chỉ khi `QuotaReserved == true`

Sau đó → `Compensating` → publish `PlaceSalesOrderFailedV1` → `Faulted`.

---

## `PlaceSalesOrderConfirmedV1` — thay đổi

Field `PaymentId` (`Guid?`) được thêm vào event này. Consumers downstream nên dùng `PaymentId` để liên kết đơn hàng với giao dịch thanh toán.

- **Saga path**: `PaymentId` luôn có giá trị (set từ `PaymentProcessedV1.PaymentId`)
- **Legacy sync handler**: `PaymentId = null` (flow cũ chưa tích hợp payment)

---

## Payment service — interface cần implement

Payment service cần:
1. Consumer subscribe `ProcessPaymentRequestedV1`
2. Xử lý giao dịch (Stripe / nội địa), dùng `OrderIdempotencyKey` để dedup
3. Publish `PaymentProcessedV1` hoặc `PaymentProcessFailedV1` về RabbitMQ

Timeout saga là **30s** — Payment service phải respond trong khoảng này; nếu không, saga tự compensate và coi là failed.

---

## Liên quan

- [Saga Orchestration Plan](saga-orchestration-plan.md)
- [TASK-02](tasks/TASK-02-saga-state-machine.md) — Implementation saga state machine
