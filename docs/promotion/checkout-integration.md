# Checkout Integration — Promotion Service

Promotion service tích hợp với Order theo **hai cơ chế song song**, tùy luồng đặt hàng:

---

## Luồng 1 — PlaceOrder Normal (Sync HTTP)

Dùng cho đơn thường (không phải flash sale). Order service gọi HTTP trực tiếp.

1. Client gọi `POST /api/v1/orders` (Gateway → Order service)
2. `PlaceOrderCommandHandler` gọi Promotion HTTP nếu `CouponCode != null`:
   - `POST http://promotion/api/v1/promotions/redeem`
3. Promotion validates code, claims flash sale slots, records usage, writes outbox event
4. Trả về `RedeemPromotionResult` với `OrderLevelDiscount`, `ItemDiscounts`, `ClaimedFlashSaleSlots`
5. Order handler dùng discount đã validated để tạo order (bỏ qua discount do client gửi)

### Request (Order → Promotion, sync)

```json
POST /api/v1/promotions/redeem
{
  "couponCode": "SUMMER20",
  "customerId": "<uuid>",
  "orderId": "<uuid>",
  "subtotal": 500000,
  "items": [
    { "productId": "<uuid>", "variantId": "<uuid>", "unitPrice": 250000, "quantity": 2 }
  ]
}
```

### Response

```json
{
  "orderLevelDiscount": 100000,
  "itemDiscounts": [],
  "appliedPromotionIds": ["<uuid>"],
  "claimedFlashSaleSlots": []
}
```

---

## Luồng 2 — PlaceSalesOrder Saga (Async RabbitMQ)

Dùng cho flash sale. Order API trả `202 Accepted` ngay, saga xử lý bất đồng bộ.

1. Order saga vào state `PromotionRedeeming` → publish `RedeemSalePromotionRequestedV1`
2. `RedeemSalePromotionRequestedConsumer` (Promotion) consume → gọi `RedeemPromotionCommand` qua MediatR
3. Publish response:
   - Thành công → `PromotionRedeemedV1` (saga chuyển `InventoryReserving`)
   - Thất bại → `PromotionRedeemFailedV1` (saga chuyển `Compensating`)

Nếu order có coupon, sau khi Inventory reserve xong:

4. Saga vào state `CouponClaiming` → publish `ClaimCouponRequestedV1`
5. `ClaimCouponRequestedConsumer` (Promotion) consume → gọi `ClaimCouponCommand` qua MediatR
6. Publish response:
   - Thành công → `CouponClaimedV1` (saga chuyển `PaymentProcessing`)
   - Thất bại → `CouponClaimFailedV1` (saga chuyển `Compensating`)

Chi tiết consumers: [promotion-saga-consumers.md](promotion-saga-consumers.md)

---

## Preview (No Side Effects)

`POST /api/v1/promotions/preview` — request shape tương tự `/redeem`, trả về `PreviewDiscountResponse` với `isEligible`, `ineligibleReason`. Không claim slot, không ghi usage, không outbox event.

---

## Outbox Event (cả hai luồng)

Sau khi redeem thành công, Promotion service publish `PromotionRedeemedV1` (legacy — `Shared.Contract.Messaging.Promotion`) qua transactional outbox → RabbitMQ. Đây là event riêng biệt với saga response event cùng tên trong `Shared.Contract.Messaging.PlaceOrderSaga`.
