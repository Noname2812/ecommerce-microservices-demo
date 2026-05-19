# Promotion Service

Server-side discount authority for the UrbanX platform. Handles creation, lifecycle management, and validation of vouchers, coupons, and flash sales.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/api/v1/promotions/` | `promotion:write` | Create promotion |
| GET | `/api/v1/promotions/` | `promotion:read` | List promotions (paginated) |
| GET | `/api/v1/promotions/{id}` | `promotion:read` | Get promotion by ID |
| PUT | `/api/v1/promotions/{id}` | `promotion:write` | Update promotion (Draft only) |
| POST | `/api/v1/promotions/{id}/activate` | `promotion:write` | Activate promotion |
| POST | `/api/v1/promotions/{id}/pause` | `promotion:write` | Pause promotion |
| POST | `/api/v1/promotions/{id}/voucher-codes` | `promotion:write` | Add voucher codes |
| POST | `/api/v1/promotions/{id}/flash-sale-items` | `promotion:write` | Add flash sale items |
| POST | `/api/v1/promotions/redeem` | Anonymous | Validate and redeem — sync, dùng cho PlaceOrder normal |
| POST | `/api/v1/promotions/preview` | Anonymous | Preview discount without side effects |
| POST | `/internal/v1/coupon-claims` | Anonymous | Claim coupon — sync, internal |
| DELETE | `/internal/v1/coupon-claims/{claimId}` | Anonymous | Release coupon claim — sync, internal |

## Message Consumers (Saga)

Consumer xử lý event từ `PlaceOrderSaga` qua RabbitMQ — tái sử dụng các MediatR command đã có:

| Consumer | Event consumed | Response events |
|---|---|---|
| `ClaimCouponRequestedConsumer` | `ClaimCouponRequestedV1` | `CouponClaimedV1` / `CouponClaimFailedV1` |
| `CouponReleaseRequestedConsumer` | `CouponReleaseRequestedV1` (compensation) | — |

Chi tiết: [promotion-saga-consumers.md](promotion-saga-consumers.md)

## Service Port

Port `5040` (manual run). Aspire assigns port dynamically at runtime.

## Dependencies

- PostgreSQL (`urbanx_promotion`)
- Redis (flash sale slot counter, distributed lock, coupon claim quota)
- RabbitMQ (publishes outbox events + saga response events; consumes saga request events)

## Config

Tuning cho message consumers được cấu hình qua `appsettings.json` (section `Promotion:Messaging`):

```json
"Promotion": {
  "Messaging": {
    "RedeemSalePromotionRequested": {
      "QueueName": "promotion-redeem-sale-promotion-requested",
      "Retry": { "RetryLimit": 3, "MinIntervalMs": 200, "MaxIntervalMs": 2000, "IntervalDeltaMs": 500 },
      "PrefetchCount": 16,
      "ConcurrentMessageLimit": 8
    },
    "ClaimCouponRequested": {
      "QueueName": "promotion-claim-coupon-requested",
      "Retry": { "RetryLimit": 3, "MinIntervalMs": 200, "MaxIntervalMs": 2000, "IntervalDeltaMs": 500 },
      "PrefetchCount": 16,
      "ConcurrentMessageLimit": 8
    }
  }
}
```

Ngoài ra không cần env var nào khác ngoài Aspire wiring chuẩn (`promotiondb`, `redis`, `messaging`).
