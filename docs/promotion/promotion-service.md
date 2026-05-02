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
| POST | `/api/v1/promotions/redeem` | Anonymous | Validate and redeem (called by Order service) |
| POST | `/api/v1/promotions/preview` | Anonymous | Preview discount without side effects |

## Service Port

Port `5040` (manual run). Aspire assigns port dynamically at runtime.

## Dependencies

- PostgreSQL (`urbanx_promotion`)
- Redis (flash sale slot counter, distributed lock)
- RabbitMQ (publishes `PromotionRedeemedV1`)

## Config

No additional env vars required beyond standard Aspire wiring (`promotiondb`, `redis`, `messaging`).
