# Place Sales Order

## Mục đích

Sales Order (Flash Sale Order) là loại đơn hàng được đặt trong các chiến dịch flash sale / khuyến mãi có giới hạn số lượng. Khác với Normal Order:

- **Quota gate**: mỗi lần đặt hàng phải trừ quota trên Redis (atomic DECR). Khi quota hết, đơn bị từ chối ngay.
- **Pricing window chặt hơn**: snapshot giá chỉ có hiệu lực 5 phút (Normal: 30 phút).
- **Per-user limit**: mỗi user chỉ mua tối đa 5 sản phẩm/item và tối đa 10 item/order trong một campaign.
- **Campaign bắt buộc**: `CampaignId` là required field.

## Endpoint

```
POST /api/v1/orders/sales
```

### Request body

| Field | Type | Required | Ghi chú |
|---|---|---|---|
| `campaignId` | `Guid` | ✓ | Flash sale campaign ID |
| `shippingAddress` | object | ✓ | Địa chỉ giao hàng |
| `shippingFee` | `decimal` | ✓ | ≥ 0 |
| `couponCode` | `string?` | — | 1-64 ký tự `[A-Za-z0-9-]` |
| `customerNote` | `string?` | — | |
| `idempotencyKey` | `string` | ✓ | UUID format "D" |
| `pricingSnapshot` | object | ✓ | `capturedAt`: UTC time của lúc lấy giá |
| `items` | array | ✓ | 1–10 items, mỗi item qty 1–5 |
| `customerEmail` | `string?` | — | |

### Response

- `201 Created` — body: `Guid` (orderId)
- Xem bảng Error Codes bên dưới cho các failure cases

## Flow xử lý (PlaceSalesOrderCommandHandler)

1. **Auth check** — `request.UserId` phải khớp với `userContext.UserId` từ Gateway header
2. **Campaign eligibility** — `ISaleEligibilityValidator.ValidateAsync(campaignId, userId, items)` — stub gọi Promotion service (luôn pass khi Promotion chưa implement endpoint)
3. **Redis quota gate** — `ISaleAllocationGate.TryReserveAsync(campaignId, userId, totalQty)` — atomic DECR trên Redis; fail fast nếu quota hết hoặc user vượt limit
4. **Parallel validation** — `IProductValidator` + `IShippingValidator` + `ISalePricingValidator` chạy đồng thời; fail fast khi bất kỳ validator nào fail
5. **Promotion redemption** (optional) — nếu có `CouponCode`, gọi `IPromotionServiceClient.RedeemAsync`
6. **Inventory reservation** — `IInventoryClient.ReserveAsync`; set `compensationContext.ReservationId`
7. **Coupon claim** (optional) — nếu có `CouponCode`, gọi `ICouponClient.ClaimAsync`
8. **Order creation** — `Order.Create(..., orderType: "Sales", campaignId: ...)` với prefix `SALE-`
9. **Confirm as sales order** — `order.SetConfirmedAsSalesOrder(reservationId, claimId, campaignId, userId, name)`
10. **Persist** — `orderRepository.Add(order)`
11. **Outbox** — `outboxWriter.WriteAsync(PlaceSalesOrderConfirmedV1)`

## Redis Quota Gate

### Key format

```
sale:{campaignId}:quota          ← tổng quota còn lại (global)
sale:{campaignId}:user:{userId}  ← số lượng user đã mua trong campaign
```

### Atomicity

Sử dụng Lua script thông qua `ICacheService.EvalAsync` để đảm bảo DECR global và INCR user quota là atomic (không có race condition giữa hai lệnh Redis riêng lẻ).

### Quota seeding

Campaign service chịu trách nhiệm seed giá trị ban đầu cho key `sale:{campaignId}:quota` trước khi campaign bắt đầu.

TTL cho user quota key: **86400 giây (1 ngày)**.

### Compensation

Khi handler thất bại sau bước 3, `PlaceSalesOrderCompensationBehavior` (MediatR pipeline behavior) sẽ ghi `SaleQuotaReleaseRequestedV1` vào outbox để hoàn lại quota. Outbox đảm bảo at-least-once delivery.

## So sánh Normal vs Sales

| | Normal | Sales |
|---|---|---|
| Pricing window | 30 phút | 5 phút |
| Max items/order | 20 | 10 |
| Max qty/item | 100 | 5 |
| Campaign required | Không | Có |
| Quota gate | Không | Redis atomic DECR |
| Order type tag | `Normal` | `Sales` |
| Order number prefix | `ORD-` | `SALE-` |
| Outbox event | `OrderConfirmedForPlaceOrderV1` | `PlaceSalesOrderConfirmedV1` |

## Error Codes

| Code | HTTP | Mô tả |
|---|---|---|
| `Order.SaleQuotaExceeded` | 409 | Global campaign quota đã hết |
| `Order.SaleUserLimitExceeded` | 409 | User vượt per-user limit (5 sản phẩm) |
| `Order.SaleCampaignInvalid` | 422 | Campaign không hợp lệ hoặc không còn hiệu lực |
| `Order.SaleWindowExpired` | 422 | Pricing snapshot quá 5 phút, cần refresh |
| `Order.SalePricingUnavailable` | 422 | Không lấy được sale price từ Promotion service |
| `Order.PriceMismatch` | 422 | Giá item không khớp với sale price của campaign |

## Config

| Setting | Giá trị | Ghi chú |
|---|---|---|
| `SaleAllocationGate.DefaultPerUserMax` | `5` | Hardcoded; có thể chuyển thành config |
| Redis user quota TTL | `86400s` | 1 ngày |
| Gateway rate limit | 20 req/min | Policy `sales-order` trên `/api/v1/orders/sales` |
