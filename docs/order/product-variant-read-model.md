# Product Variant Read Model

## Mục đích

Order service trước đây verify thông tin sản phẩm/variant bằng cách gọi Catalog qua HTTP (`ICatalogServiceClient` + Refit + Redis caching decorator) tại mỗi lần place-order. Cách này coupling chéo runtime (Catalog xuống → Order không đặt được đơn) và không tận dụng integration events Catalog đã publish qua MassTransit EF Outbox.

Read model `product_variant_view` là projection cục bộ trong Order DB, được sync qua 6 integration events từ Catalog. Saga `PlaceOrderNormal` / `PlaceSalesOrder` validate đơn hàng chỉ với 1 query Postgres (no HTTP).

## Schema

Bảng: `read.product_variant_view`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| variant_id | uuid (PK) | ID variant của Catalog |
| product_id | uuid (idx) | FK logic — không enforce DB |
| product_name | varchar(500) | denormalize từ Product |
| product_is_active | bool | true nếu Product.Status == "ACTIVE" |
| sku | varchar(100) | |
| variant_name | varchar(500) | nullable |
| image_url | varchar(1000) | nullable |
| price | numeric(18,2) | giá hiện tại từ Catalog |
| is_active | bool | variant active flag |
| seller_id | uuid (idx) | |
| seller_name | varchar(255) | |
| seller_is_active | bool | hiện hardcode `true` (Catalog chưa publish seller-deactivate event) |
| row_version | int | bản version từ Catalog (CAS check khi place order) |
| projection_version | int | counter cục bộ, tăng 1 mỗi lần upsert |
| updated_at | timestamptz | thời điểm refresh gần nhất |
| deleted_at | timestamptz? | soft-delete khi nhận `ProductVariantDeletedV1` |

## Cơ chế Version (strict optimistic concurrency)

1. Frontend / Postman load variant từ Catalog API → đọc field `RowVersion` (int).
2. Khi place order, client gửi `PlaceOrderLineDto.Version = <RowVersion>` cho từng item.
3. Saga `PlaceOrderNormal` → `ValidateThroughCatalogAsync` query read model:
   - Nếu `variant.RowVersion != item.Version` → reject với `Variant.VersionMismatch`.
   - Mismatch ⇒ variant đã được Catalog cập nhật giữa lúc client load và lúc submit ⇒ client phải reload + xác nhận lại.
4. Defense in depth: thêm price check 1% tolerance so với `variant.Price`. Version match thì price luôn pass; mismatch chỉ xảy ra nếu client gửi sai UnitPrice.

## Sync Pipeline

```
Catalog (write side)
  Command Handler              EF Transaction
  ┌──────────────────────────────────────────┐
  │ product.AddVariant(...)                  │
  │ await repo.SaveAsync(...)                │
  │ await eventPublisher.PublishAsync(...)   │
  └──────────────────────────────────────────┘
                │                              MT bus outbox stage
                ▼
        outbox_message (RabbitMQ)
                │
                ▼
Order (read side) — 6 consumers in Infrastructure/Messaging/<Event>/
  IConsumer<TEvent>  →  ISender.Send(RefreshProductVariantProjectionCommand | MarkProductVariantDeletedCommand | UpdateProductStatusProjectionCommand)
                     →  ProductVariantReadModelRepository.UpsertAsync/MarkDeletedAsync/UpdateProductStatusAsync
                     →  EF SaveChanges  →  read.product_variant_view
```

### 6 Events Order subscribe

| Event (Shared.Contract) | Consumer | Action |
|---|---|---|
| `ProductIntegrationEvents.ProductCreatedV1` | `ProductCreatedReadModelConsumer` | Foreach variants → refresh row (full info từ payload) |
| `ProductUpdateIntegrationEvents.ProductInfoUpdatedV1` | `ProductInfoUpdatedReadModelConsumer` | Foreach `ActiveVariants` → refresh row (ProductName + variant snapshot mới) |
| `ProductUpdateIntegrationEvents.ProductStatusChangedV1` | `ProductStatusChangedReadModelConsumer` | Bulk update `product_is_active` cho mọi variant của product |
| `ProductUpdateIntegrationEvents.ProductVariantAddedV1` | `ProductVariantAddedReadModelConsumer` | Tra cứu sibling variant để lấy ProductName/SellerName → refresh row |
| `ProductUpdateIntegrationEvents.ProductVariantUpdatedV1` | `ProductVariantUpdatedReadModelConsumer` | Tra cứu sibling → refresh row |
| `ProductUpdateIntegrationEvents.ProductVariantDeletedV1` | `ProductVariantDeletedReadModelConsumer` | Set `deleted_at = NOW()` + `is_active = false` |

### Concurrency / out-of-order guard

`ProductVariantReadModelRepository.UpsertAsync` skip nếu `existing.RowVersion > incoming.RowVersion` — bảo vệ khi event đến không đúng thứ tự. `projection_version` (local counter) tăng đều mỗi upsert giúp debug.

### Note: SellerName cho variant-level events

`ProductVariantAddedV1` / `ProductVariantUpdatedV1` không carry `SellerName` (chỉ có `SellerId` ở level event và variant snapshot). Consumer tra cứu sibling variant cùng `ProductId` trong read model để inherit `SellerName/ProductName/ProductIsActive/SellerIsActive`. Nếu chưa có sibling (vd: event arrive trước `ProductCreatedV1`), consumer log warning và skip — `ProductCreatedV1` đến sau sẽ tạo row.

## Configuration

```jsonc
// appsettings.json — Order
{
  "Order": {
    "Messaging": {
      "ProductProjection": {
        "PrefetchCount": 32,
        "ConcurrentMessageLimit": 8,
        "Retry": {
          "RetryLimit": 3,
          "MinIntervalMs": 200,
          "MaxIntervalMs": 2000,
          "IntervalDeltaMs": 500
        }
      }
    }
  }
}
```

Shared options class: `ProductProjectionConsumerOptions` (section `Order:Messaging:ProductProjection`). Tất cả 6 consumer dùng chung 1 bộ tuning vì workload đồng nhất (1 upsert/event).

## Bootstrap & Backfill

- Read model trống lúc start: 10 seed row được fill bởi `OrderReadModelSeeder.SeedIfEmptyAsync` (Development mode only) — cùng VariantId formula với Catalog/Inventory seeder để cross-service test happy path. `RowVersion = 1`.
- Không backfill data có sẵn từ Catalog: hot data tự sync khi có event mới. Test local với 10 seed variant + tạo product mới qua Catalog API → quan sát flow.

## Migration: từ HTTP Catalog Client sang Read Model

| Trước | Sau |
|---|---|
| Saga gọi `ICatalogServiceClient.GetVariantsAsync(ids)` qua Refit | Saga gọi `IProductVariantReadModelRepository.GetByIdsAsync(ids)` |
| Variant data có TTL Redis cache (stale window không rõ) | Variant data đồng bộ theo event, version-tracked |
| Order depends on Catalog runtime để place đơn | Order độc lập runtime — Catalog xuống vẫn place được nếu read model đủ data |
| Frontend không cần version | Frontend phải đọc + submit `Version` trong từng order line |

### Files đã xóa

- `src/Services/Order/UrbanX.Order.Application/Clients/ICatalogServiceClient.cs` (+ DTOs `CatalogVariantInfo`, `CatalogProductValidationDto`, `CatalogPriceValidationDto`)
- `src/Services/Order/UrbanX.Order.Infrastructure/Services/CatalogServiceClient.cs`
- `src/Services/Order/UrbanX.Order.Infrastructure/Services/CachingCatalogServiceClient.cs`
- `src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Options/CatalogClient*.cs` (3 files)
- `tests/UrbanX.Services.Order.UnitTests/Infrastructure/Services/CatalogServiceClient*.cs` (3 test files)

### Files mới

- `src/Services/Order/UrbanX.Order.Domain/Models/ProductVariantReadModel.cs`
- `src/Services/Order/UrbanX.Order.Domain/Repositories/IProductVariantReadModelRepository.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Repositories/ProductVariantReadModelRepository.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Configurations/ProductVariantReadModelConfiguration.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Seeding/OrderReadModelSeeder.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Migrations/20260524014359_AddProductVariantReadModel.cs`
- `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/RefreshProductVariantProjection/`
- `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/MarkProductVariantDeleted/`
- `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/UpdateProductStatusProjection/`
- `src/Services/Order/UrbanX.Order.Infrastructure/Messaging/Product*/` (6 folders × 2 file = 12 files)
- `src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Options/ProductProjectionConsumerOptions{,Validator}.cs`

## Domain Errors

- `OrderErrors.VariantVersionMismatch(variantId, clientVersion, currentVersion)` — code `Variant.VersionMismatch`, status 400.
- `OrderErrors.VariantNotInReadModel(variantId)` — code `Variant.NotInReadModel`, status 400. Reject khi event chưa kịp sync; client retry.

## Test local

```bash
cd src/AppHost/UrbanX.AppHost
dotnet run
```

Aspire bật xong:

```sql
-- Seed 10 row sẽ xuất hiện
SELECT variant_id, product_name, sku, price, row_version
FROM read.product_variant_view
ORDER BY product_name;
```

Test happy path qua Gateway/Postman với `VariantId = SeedVariantId(1)`, `Version = 1`, `UnitPrice = 110_000`.

Test version mismatch: gửi `Version = 99` → expect `Variant.VersionMismatch`.

Test sync: tạo product mới qua Catalog API → row mới xuất hiện trong `read.product_variant_view` của Order DB sau khi MT bus outbox publish event.

## Schema bảng cross-service

`SellerIsActive` hiện hardcode `true` trong tất cả consumer vì Catalog chưa publish event `SellerDeactivated`. Khi seller deactivation flow được implement, thêm 1 consumer riêng + 1 command để update field này.
