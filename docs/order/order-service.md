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

### Normal Order

| Method | Path | Command/Query | Permission |
|---|---|---|---|
| POST | `/api/v1/orders` | `PlaceOrderCommand` | `order:write` (Own) |
| GET | `/api/v1/orders/my` | `ListMyOrdersQuery` | `order:read` (Own) |
| GET | `/api/v1/orders/{id}` | `GetOrderByIdQuery` | `order:read` (Own) |
| PUT | `/api/v1/orders/{id}/cancel` | `CancelOrderCommand` | `order:write` (Own) |

### Flash Sale Order (async)

| Method | Path | Command/Query | Permission | Response |
|---|---|---|---|---|
| POST | `/api/v1/orders/sales` | `PlaceSalesOrderCommand` | `order:write` (Own) | `202 Accepted` + `Location` header |
| GET | `/api/v1/orders/sales/{id}/status` | `GetSalesOrderStatusQuery` | `order:read` (Own) | `200 OK` — `SalesOrderStatusDto` |

`POST /sales` trả về ngay sau khi save Order(Pending) + publish `PlaceSalesOrderRequestedV1`. Client poll `GET /sales/{id}/status` để theo dõi saga tiến trình đến `Confirmed` hoặc `Faulted`.

Xem chi tiết: [place-sales-order.md](place-sales-order.md)

## Integration Events Published

| Event | Trigger | Transport |
|---|---|---|
| `OrderCreatedV1` | Normal order placed | Outbox → RabbitMQ |
| `OrderCancelledV1` | Order cancelled | Outbox → RabbitMQ |
| `PlaceSalesOrderRequestedV1` | Sales order accepted (202) | Outbox → RabbitMQ |
| `PlaceSalesOrderConfirmedV1` | Saga completes successfully | Saga → RabbitMQ |
| `PlaceSalesOrderFailedV1` | Saga faults | Saga → RabbitMQ |

## Saga

`PlaceSalesOrderSagaStateMachine` (MassTransit, EF Core persistence) orchestrates the async flow:

```
Initial → PromotionRedeeming → InventoryReserving → [CouponClaiming] → PaymentProcessing → Confirming → Completed
                                                                                              ↓ (any step fail)
                                                                                          Compensating → Faulted
```

State persisted in `place_sales_order_saga_states` table (same `OrderDbContext`).  
Xem chi tiết: [saga-orchestration-plan.md](saga-orchestration-plan.md)

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
