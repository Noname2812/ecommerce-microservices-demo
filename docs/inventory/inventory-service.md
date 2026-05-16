# Inventory Service

## Mục đích

Quản lý tồn kho sản phẩm: số lượng theo SKU/variant, theo warehouse, reservation, và điều chỉnh kho. Tham gia saga choreography trong order flow (Order → Inventory → Payment → Merchant).

## Thông tin cơ bản

| Thuộc tính | Giá trị |
|---|---|
| Port | Dynamic (Aspire) |
| Database | PostgreSQL (`urbanx_inventory`) |
| Connection string name | `inventorydb` |
| Messaging | RabbitMQ via MassTransit |
| Outbox | Có (at-least-once delivery) |
| Status | Active (scaffold) |

## Projects

| Project | Trách nhiệm |
|---|---|
| `UrbanX.Inventory.Domain` | Entities, value objects, repository interfaces |
| `UrbanX.Inventory.Application` | Commands, queries, handlers, validators, errors |
| `UrbanX.Inventory.Persistence` | EF Core DbContext, migrations, repos |
| `UrbanX.Inventory.Infrastructure` | Placeholder — external integrations |
| `UrbanX.Inventory.API` | Carter endpoints, Program.cs |

## Endpoints

Base URL: `/api/v{version}/inventory/items`

Endpoints sẽ được thêm theo từng use case bằng skill `add-command` / `add-query`.

## Gateway Route

```
/api/v{version}/inventory/** → inventory cluster
```

## EF Migrations

```bash
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add InitialCreate
```

## Integration Events

Contracts trong `Shared.Contract/Messaging/PlaceOrder/` và `Shared.Contract/Messaging/PlaceOrderSaga/`.

| Consumer | Event | Namespace | Config section |
|---|---|---|---|
| `InventoryReleaseRequestedConsumer` | `InventoryReleaseRequestedV1` | `PlaceOrder` | `Inventory:Messaging:InventoryReleaseRequested` |
| `ReserveInventoryRequestedConsumer` | `ReserveInventoryRequestedV1` | `PlaceOrderSaga` | `Inventory:Messaging:ReserveInventoryRequested` |

### ReserveInventoryRequestedConsumer (saga)

Nhận `ReserveInventoryRequestedV1` từ `PlaceSalesOrderSaga`. Delegate sang `ReserveInventoryRequestedProcessor` → `ReserveInventoryCommand` (MediatR). Publish `InventoryReservedV1` hoặc `InventoryReserveFailedV1` tùy kết quả.

- Idempotency: handler tự xử lý qua `OrderIdempotencyKey` (`:inv` suffix)
- Concurrency retry: `IConcurrencyRetriableCommand` xử lý xmin conflict tự động
- Retry broker-level: exponential 3 lần (config `Retry.RetryLimit/MinIntervalMs/MaxIntervalMs/IntervalDeltaMs`)
- Throughput: `PrefetchCount=32`, `ConcurrentMessageLimit=16` (default)

Chi tiết: [reserve-inventory-consumer.md](reserve-inventory-consumer.md)

### InventoryReleaseRequestedConsumer (compensation)

Consumer bù trừ cho PlaceOrder normal và PlaceSalesOrder saga. Đọc cấu hình từ `Inventory:Messaging:InventoryReleaseRequested`.
