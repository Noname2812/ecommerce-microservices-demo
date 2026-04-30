# Order Service

**Port**: dynamic (Aspire) / 5002 (manual)  
**Database**: PostgreSQL (`urbanx_order`)  
**Outbox**: yes  
**Messaging**: RabbitMQ via MassTransit

---

## Mục đích

Quản lý vòng đời đơn hàng. Order items và shipping address là **snapshot** tại thời điểm đặt hàng — không thay đổi dù product bị edit sau đó.

## Status Flow

```
PENDING → CONFIRMED → PROCESSING → SHIPPED → DELIVERED → COMPLETED
PENDING → CANCELLED
```

## Endpoints

| Method | Path | Command/Query | Permission |
|---|---|---|---|
| POST | `/api/v1/orders` | `PlaceOrderCommand` | `order:write` (Own) |
| GET | `/api/v1/orders/my` | `ListMyOrdersQuery` | `order:read` (Own) |
| GET | `/api/v1/orders/{id}` | `GetOrderByIdQuery` | `order:read` (Own) |
| PUT | `/api/v1/orders/{id}/confirm` | `ConfirmOrderCommand` | `order:write` (All — Admin) |
| PUT | `/api/v1/orders/{id}/cancel` | `CancelOrderCommand` | `order:write` (Own) |

## Integration Events Published

| Event | Trigger |
|---|---|
| `OrderCreatedV1` | Order placed successfully |
| `OrderConfirmedV1` | Admin confirms order |
| `OrderCancelledV1` | Order cancelled (customer or admin) |

Events published via Transactional Outbox → RabbitMQ.

## Integration Events Consumed

_(không có trong MVP — sẽ thêm khi Payment/Inventory service active)_

- `PaymentCompleted` → update `payment_status`, `payment_reference`
- `StockReserved` → advance to `PROCESSING`

## Domain Models

- `Order` — aggregate root, snapshot customer + address info
- `OrderItem` — snapshot product/variant/price tại thời điểm đặt hàng
- `OrderStatusHistory` — append-only audit trail

## Idempotency

`PlaceOrderCommand` nhận optional `IdempotencyKey`. Nếu đã có order với key này, trả về `orderId` hiện có mà không tạo mới.

## Chạy migration

```bash
cd src/Services/Order/UrbanX.Order.Persistence
dotnet ef migrations add InitialCreate
dotnet ef database update
```
