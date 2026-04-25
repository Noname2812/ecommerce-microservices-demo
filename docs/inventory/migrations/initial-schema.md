# Inventory Service — Initial Schema

## Migration: `InitialCreate`

Tạo toàn bộ schema ban đầu cho Inventory service.

## Tables

### `warehouses`

Kho hàng vật lý. Address lưu dạng JSONB.

| Column | Type | Ghi chú |
|---|---|---|
| `id` | UUID PK | App-assigned |
| `name` | VARCHAR(255) NOT NULL | — |
| `code` | VARCHAR(50) NOT NULL UNIQUE | Mã kho |
| `address` | JSONB NOT NULL | `{Street, Ward, District, City, Province, Country, ZipCode}` |
| `is_active` | BOOLEAN DEFAULT TRUE | — |

### `inventory_items`

Tồn kho theo variant × warehouse. Đây là nguồn sự thật duy nhất về stock.

| Column | Type | Ghi chú |
|---|---|---|
| `id` | UUID PK | — |
| `product_id` | UUID NOT NULL | Denormalized từ Catalog, không có FK |
| `product_name` | VARCHAR(500) NOT NULL | Denormalized |
| `variant_id` | UUID NOT NULL | Denormalized từ Catalog, không có FK |
| `variant_sku` | VARCHAR(100) NOT NULL | Denormalized |
| `variant_name` | VARCHAR(255) | Denormalized |
| `warehouse_id` | UUID FK → warehouses | Nullable |
| `quantity_on_hand` | INT NOT NULL | Tổng hàng trong kho |
| `quantity_reserved` | INT NOT NULL | Đang giữ bởi pending orders |
| `quantity_available` | INT GENERATED STORED | `quantity_on_hand - quantity_reserved` |
| `reorder_point` | INT DEFAULT 10 | Ngưỡng cảnh báo tồn kho thấp |
| `reorder_quantity` | INT DEFAULT 50 | Số lượng đề xuất khi đặt lại |
| `updated_at` | TIMESTAMPTZ NOT NULL | — |

**Unique constraint:** `(variant_id, warehouse_id)`  
**Indexes:** `product_id`, `variant_id`, `warehouse_id`

### `inventory_reservations`

Giữ hàng khi order được tạo. Auto-release khi order expire/cancel.

| Column | Type | Ghi chú |
|---|---|---|
| `id` | UUID PK | — |
| `inventory_item_id` | UUID FK → inventory_items CASCADE | — |
| `order_id` | UUID NOT NULL | Cross-service |
| `order_item_id` | UUID NOT NULL | Cross-service |
| `quantity` | INT NOT NULL | — |
| `status` | VARCHAR(20) DEFAULT 'RESERVED' | RESERVED \| CONFIRMED \| RELEASED \| CANCELLED |
| `expires_at` | TIMESTAMPTZ NOT NULL | Auto-release deadline |
| `created_at` | TIMESTAMPTZ NOT NULL | — |
| `updated_at` | TIMESTAMPTZ NOT NULL | — |

**Indexes:** `order_id`, composite `(status, expires_at)`

### `stock_movements`

Audit trail đầy đủ — append-only, không bao giờ xóa.

| Column | Type | Ghi chú |
|---|---|---|
| `id` | UUID PK | — |
| `inventory_item_id` | UUID FK → inventory_items RESTRICT | — |
| `movement_type` | VARCHAR(50) NOT NULL | RECEIPT \| SALE \| RETURN \| ADJUSTMENT \| TRANSFER_IN \| TRANSFER_OUT \| RESERVATION \| RELEASE |
| `quantity_change` | INT NOT NULL | Positive = nhập, Negative = xuất |
| `quantity_before` | INT NOT NULL | — |
| `quantity_after` | INT NOT NULL | — |
| `reference_type` | VARCHAR(50) | ORDER \| PURCHASE_ORDER \| MANUAL_ADJUSTMENT |
| `reference_id` | UUID | Cross-service ID |
| `note` | TEXT | — |
| `created_by_id` | UUID | Cross-service, không có FK |
| `created_by_name` | VARCHAR(255) | Denormalized |
| `created_at` | TIMESTAMPTZ NOT NULL | — |

**Index:** `(inventory_item_id, created_at DESC)`

### `outbox_messages`

Quản lý bởi `Shared.Outbox` — không định nghĩa thủ công.

## Events publish (từ Outbox)

| Event | Trigger |
|---|---|
| `StockReserved` | Reservation tạo thành công |
| `StockReleased` | Reservation cancelled/expired |
| `StockConfirmed` | Order confirmed → reservation chuyển CONFIRMED |
| `StockUpdated` | Cập nhật `quantity_on_hand` thủ công |
| `LowStockAlert` | `quantity_available` xuống dưới `reorder_point` |

## Events consume

| Event | Source | Action |
|---|---|---|
| `ProductCreated` | Catalog | Tạo `InventoryItem` với `quantity_on_hand = 0` |
| `VariantUpdated` | Catalog | Sync `product_name`, `variant_sku`, `variant_name` |
| `OrderCancelled` | Order | Release reservation → emit `StockReleased` |

## Chạy migration

```bash
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add InitialCreate
dotnet ef database update
```
