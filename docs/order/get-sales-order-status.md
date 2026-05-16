# Get Sales Order Status

## Mục đích

Endpoint dành cho client poll để theo dõi tiến trình xử lý async của Flash Sale Order sau khi `POST /api/v1/orders/sales` trả về `202 Accepted`.

## Endpoint

```
GET /api/v1/orders/sales/{id}/status
```

### Path params

| Param | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | Order ID trả về từ `POST /sales` |

### Response `200 OK`

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderStatus": "Pending",
  "sagaState": "InventoryReserving",
  "reservationId": null,
  "couponClaimId": null,
  "failureStep": null,
  "failureReason": null,
  "updatedAt": "2026-05-17T10:30:00Z"
}
```

| Field | Mô tả |
|---|---|
| `orderStatus` | Trạng thái của Order entity: `Pending` → `Confirmed` / `Cancelled` |
| `sagaState` | Trạng thái hiện tại của saga instance: xem bảng bên dưới |
| `reservationId` | ID reservation trả về từ Inventory service (null khi chưa tới bước này) |
| `couponClaimId` | ID claim trả về từ Coupon service (null nếu không có coupon hoặc chưa tới bước này) |
| `failureStep` | Bước saga thất bại (null khi saga đang chạy hoặc hoàn thành) |
| `failureReason` | Lý do thất bại (null tương tự) |
| `updatedAt` | `saga.UpdatedAt` nếu saga tồn tại, fallback về `order.UpdatedAt` |

### Saga states

| State | Ý nghĩa |
|---|---|
| `Pending` | Saga chưa được khởi tạo (event chưa tới consumer) |
| `PromotionRedeeming` | Đang chờ Promotion service apply coupon / flash sale slots |
| `InventoryReserving` | Đang chờ Inventory service reserve hàng |
| `CouponClaiming` | Đang chờ Coupon service claim coupon |
| `PaymentProcessing` | Đang chờ Payment service xử lý |
| `Confirming` | Đang cập nhật Order → Confirmed |
| `Completed` | Toàn bộ flow thành công; `orderStatus` = `Confirmed` |
| `Compensating` | Có bước thất bại, đang rollback |
| `Faulted` | Saga kết thúc với lỗi; `failureStep` và `failureReason` có giá trị |

### Error responses

| HTTP | Code | Mô tả |
|---|---|---|
| 404 | `ORDER_NOT_FOUND` | Order không tồn tại |
| 403 | `ORDER_FORBIDDEN` | Order thuộc về user khác |

## Polling strategy (client)

```
POST /api/v1/orders/sales
  → 202 Accepted { orderId, status: "Pending" }
  → Location: /api/v1/orders/sales/{id}/status

while sagaState not in ["Completed", "Faulted"]:
    sleep(500ms)
    GET /api/v1/orders/sales/{id}/status
```

Timeout đề xuất: **30 giây**. Nếu quá thời gian, hiển thị trạng thái hiện tại và cho phép user refresh thủ công.

## Implementation

- **Query**: `GetSalesOrderStatusQuery(Guid OrderId)`
- **Handler**: `GetSalesOrderStatusQueryHandler` — inject `ISalesOrderStatusQuery` (port)
- **Persistence impl**: `SalesOrderStatusQuery` — EF Core LEFT JOIN `orders ⟕ place_sales_order_saga_states on o.id = s.order_id`
- **Ownership check**: `order.UserId != userContext.UserId` → 403
