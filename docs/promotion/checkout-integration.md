# Checkout Integration — Promotion Service

## Flow

1. Client calls `POST /api/v1/orders` (via Gateway → Order service)
2. `PlaceOrderCommandHandler` detects `CouponCode != null`
3. Order service calls Promotion service directly (server-to-server, bypasses Gateway):
   - `POST http://promotion/api/v1/promotions/redeem`
4. Promotion service validates the code, claims flash sale slots, records usage, writes outbox event
5. Returns `PromotionRedeemResponse` with `OrderLevelDiscount` and `ItemDiscounts`
6. Order handler uses server-validated discounts to create the order (ignores client-provided `CouponDiscount`)

## Request Shape (Order → Promotion)

```json
POST /api/v1/promotions/redeem
{
  "orderId": null,
  "customerId": "<uuid>",
  "couponCode": "SUMMER20",
  "subTotal": 500000,
  "items": [
    { "variantId": "<uuid>", "productId": "<uuid>", "quantity": 2, "unitPrice": 250000 }
  ]
}
```

## Response Shape

```json
{
  "orderLevelDiscount": 100000,
  "itemDiscounts": [],
  "appliedPromotionIds": ["<uuid>"]
}
```

## Preview (No Side Effects)

`POST /api/v1/promotions/preview` — same request shape, returns `PreviewDiscountResponse` with `isEligible`, `ineligibleReason`. No slot claim, no usage record, no outbox event.

## Outbox Event

After successful redemption, Promotion service publishes `PromotionRedeemedV1` via transactional outbox → RabbitMQ.
