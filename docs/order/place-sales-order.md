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

- `202 Accepted` — body: `{ orderId: Guid, status: "Pending" }`, header `Location: /api/v1/orders/sales/{id}/status`
- Client poll `GET /api/v1/orders/sales/{id}/status` để theo dõi tiến trình async
- Xem bảng Error Codes bên dưới cho các failure cases

## Flow xử lý (async — TASK-05)

Handler (`PlaceSalesOrderCommandHandler`) chỉ xử lý **sync portion** và publish trigger event. Toàn bộ Promotion / Inventory / Coupon được saga xử lý bất đồng bộ qua RabbitMQ.

1. **Auth check** — `userContext.UserId` phải có giá trị
2. **Idempotency guard** — tra Redis theo `idempotencyKey`; trả về `orderId` cũ nếu đã tồn tại (fail-open khi Redis unavailable)
3. **Campaign eligibility** — `ISaleEligibilityValidator.ValidateAsync(campaignId, userId, items)`
4. **Redis quota gate** — `ISaleAllocationGate.TryReserveAsync(campaignId, userId, totalQty)` — atomic Lua DECR; fail fast nếu hết quota / vượt user limit; set `salesCompensationContext` để compensate nếu bước sau fail
5. **Parallel validation** — `IProductValidator` + `IShippingValidator` + `ISalePricingValidator` chạy đồng thời
6. **Save Order(Pending) + outbox** — `Order.Create(orderType: "Sales", campaignId)` → `orderRepository.Add(order)` → `outboxWriter.WriteAsync(PlaceSalesOrderRequestedV1)` trong cùng một EF transaction (qua `TransactionPipelineBehavior`)
7. **Set idempotency cache** — best-effort; pipeline idempotency vẫn bảo vệ nếu bước này fail

Saga (`PlaceSalesOrderSagaStateMachine`) nhận `PlaceSalesOrderRequestedV1` và xử lý tuần tự:
→ PromotionRedeeming → InventoryReserving → [CouponClaiming] → PaymentProcessing → Confirming

Xem chi tiết saga: [saga-orchestration-plan.md](saga-orchestration-plan.md)

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

Khi handler thất bại sau bước quota gate (bước 4), `PlaceSalesOrderCompensationBehavior` (MediatR pipeline behavior) ghi `SaleQuotaReleaseRequestedV1` vào compensation outbox để hoàn lại quota. Saga sở hữu compensation cho Inventory / Coupon / Promotion — handler không cần xử lý.

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
| API response | `201 Created` (orderId) | `202 Accepted` (orderId + Location) |
| Trigger event | `OrderConfirmedForPlaceOrderV1` | `PlaceSalesOrderRequestedV1` (handler) |
| Outbox event (final) | `OrderConfirmedForPlaceOrderV1` | `PlaceSalesOrderConfirmedV1` (saga, sau payment) |

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
