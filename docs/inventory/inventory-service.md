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

Contract place-order: `Shared.Contract/Messaging/PlaceOrder/` (ví dụ `InventoryReleaseRequestedV1`). Consumer compensation đọc cấu hình queue/retry/throughput từ appsettings `Inventory:Messaging:InventoryReleaseRequested` (class `InventoryReleaseRequestedConsumerOptions`), validate lúc startup qua `InventoryReleaseRequestedConsumerOptionsValidator` (`ValidateOnStart`).
