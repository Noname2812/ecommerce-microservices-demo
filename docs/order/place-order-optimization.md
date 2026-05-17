# Place Order — Tiered Catalog Validation

## Mục đích

Tối ưu throughput & latency cho flow Place Order (Normal + Flash Sale) bằng cách
thay 2 sync HTTP call Catalog (validate-products, variant-prices) bằng lookup
3 tầng cache + local read model, vẫn giữ CP guarantee tổng thể qua saga.

## Kiến trúc tổng quan

```
[Validators] ─→ IProductSnapshotCache
                  ├─ L1: Redis        (TTL 120s)
                  ├─ L2: Local model  (read.catalog_snapshots, Dapper)
                  └─ L3: HTTP fallback (Catalog, timeout 300ms)
                                ↑
[Catalog events] ─→ 6 consumers ─→ upsert L2 + invalidate L1
```

L2 luôn có sau khi service warm up. L3 chỉ dùng khi cả L1 và L2 đều miss
(variant mới chưa nhận event projection).

## Components

| Lớp | File | Mô tả |
|---|---|---|
| Abstraction | `Order.Application/Abstractions/Catalog/IProductSnapshotCache.cs` | 2 method: GetProducts, GetVariantPrices + Invalidate |
| Abstraction | `Order.Application/Abstractions/Catalog/ProductSnapshot.cs` | Record types: ProductSnapshot, VariantPriceSnapshot |
| L1+L2+L3 impl | `Order.Infrastructure/Services/RedisProductSnapshotCache.cs` | 3-tier lookup, ghi metric mỗi tier |
| L2 reader | `Order.Application/ReadModels/ICatalogSnapshotReader.cs` + `DapperCatalogSnapshotReader` | Dapper SELECT từ `read.catalog_snapshots` |
| L2 writer | `Order.Application/ReadModels/ICatalogSnapshotWriter.cs` + `DapperCatalogSnapshotWriter` | UPSERT + DELETE + UPDATE-status, version-guarded |
| Entity | `Order.Domain/ReadModels/CatalogSnapshot.cs` | EF entity cho migration table |
| Inbox dedup | `Order.Domain/Models/ProcessedEvent.cs` + `ProcessedEventRepository` | EventId-based dedup |
| Consumer base | `Order.Application/Messaging/Catalog/CatalogProjectionConsumerBase.cs` | Dedup + transactional upsert + post-commit cache invalidate |
| 6 consumers | `Order.Application/Messaging/Catalog/Product*Consumer.cs` | Subscribe `ProductCreated`, `ProductInfoUpdated`, `ProductStatusChanged`, `ProductVariantAdded/Updated/Deleted` |
| Metrics | `Order.Application/Telemetry/OrderValidatorMetrics.cs` | Counter + Histogram, meter `UrbanX.Order` |
| Constants | `Order.Application/Constants/CatalogProjectionConstants.cs` | Cache keys, meter/metric names, tag values |
| Options | `Order.Infrastructure/DependencyInjection/Options/CatalogSnapshotOptions.cs` | Section `Order:CatalogSnapshot` — TTL + HTTP timeout |
| HTTP fallback | `Order.Infrastructure/Services/CatalogServiceClient.cs` | timeout from options, exception → `OrderErrors.CatalogUnavailable` |
| Error mapping | `Order.API/Abstractions/ApiEndpoint.cs` | CATALOG_UNAVAILABLE → HTTP 503 |

## Integration events tiêu thụ

Tất cả các event sau từ Catalog (đã có sẵn trong `Shared.Contract/Messaging/Catalog`):

- `ProductCreatedV1` — insert N variant rows
- `ProductInfoUpdatedV1` — upsert active variants với status snapshot
- `ProductStatusChangedV1` — update `product_is_active` cho variant trong AffectedVariantIds
- `ProductVariantAddedV1` — upsert 1 variant
- `ProductVariantUpdatedV1` — upsert 1 variant (SKU/price/isActive)
- `ProductVariantDeletedV1` — delete 1 variant

## Database

Table mới (qua migration `AddCatalogSnapshotReadModel`):

```sql
-- read.catalog_snapshots
variant_id          uuid PRIMARY KEY
product_id          uuid NOT NULL
sku                 varchar(100) NOT NULL
product_is_active   boolean NOT NULL
variant_is_active   boolean NOT NULL
current_price       numeric(18,2) NOT NULL
projection_version  bigint NOT NULL    -- = OccurredOn.UtcTicks, version-guard cho out-of-order events
updated_at          timestamptz NOT NULL
INDEX (product_id)

-- public.processed_events (inbox dedup)
event_id        uuid PRIMARY KEY
event_type      varchar(500) NOT NULL
processed_at    timestamptz NOT NULL
INDEX (processed_at)
```

## Idempotency / consistency model

- **Inbox dedup**: trước khi project, consumer check `ProcessedEvent.ExistsAsync(eventId)` → skip nếu đã xử lý
- **Out-of-order protection**: UPSERT có `WHERE projection_version < EXCLUDED.projection_version` — event cũ hơn không ghi đè
- **Transactional**: 1 EF transaction wrap (a) projection upsert + (b) ProcessedEvent insert → all-or-nothing
- **Post-commit invalidation**: sau khi DB commit, invalidate Redis key tương ứng (best-effort; TTL 120s là safety net)

## CAP guarantee

| Data | Behavior | CP/AP |
|---|---|---|
| Catalog validation (L1+L2) | Eventual consistent, ~secs lag | AP |
| Pricing tolerance 1% / 30min | Đã có trong PricingValidator | AP với business bound |
| Inventory reservation | Saga `ReserveInventoryCommand` PG row lock + xmin | **CP** |
| Coupon claim | Saga step + ProcessedEvent dedup | **CP** |
| Sale quota (flash sale) | Redis Lua atomic (SaleAllocationGate) | **CP** |

**Catalog đã giảm xuống AP** — vì source of truth cuối cùng nằm ở Inventory
reservation step trong saga. Catalog projection lag chỉ ảnh hưởng UX (chấp nhận
order có thể fail ở reservation step và compensate) — không ảnh hưởng tính
nhất quán tổng thể.

## Observability

Meter `UrbanX.Order` (đã register vào OpenTelemetry):

- **Counter** `order.validator.source` — tag `validator` ∈ {product, pricing}, tag `source` ∈ {cache_hit, local_hit, http_fallback, failed}
- **Histogram** `order.validator.duration_ms` — tag `validator`

**Alert thresholds đề xuất:**

- `http_fallback` rate > 5% trong 5 phút → projection lag (RabbitMQ backlog hoặc consumer chết)
- P99 `order.validator.duration_ms` > 50ms → cache hit rate thấp / Redis chậm
- `failed` rate > 0 → Catalog HTTP timeout đồng thời L1+L2 đều miss (cần kiểm tra event ingestion)

## Configuration

**Options class:** `CatalogSnapshotOptions` (Infrastructure) — bind từ section `Order:CatalogSnapshot` qua `BindConfiguration` + `ValidateDataAnnotations` + `ValidateOnStart`.

```json
"Order": {
  "CatalogSnapshot": {
    "CacheTtlSeconds": 120,                     // L1 Redis TTL
    "HttpFallbackTimeoutMilliseconds": 300      // L3 HTTP timeout
  }
}
```

**Constants:** mọi identifier (cache key prefixes, meter / metric names, tag keys, tag values) tập trung trong `Order.Application/Constants/CatalogProjectionConstants.cs`. Error code (`CATALOG_UNAVAILABLE`) sống cùng definition trong `OrderErrors.CatalogUnavailable` (Domain).

- Cache key format: `order:catalog:product:{id}` / `order:catalog:variant:{id}` (instance prefix từ `Shared.Cache` options ghép phía trước).

## Migration & rollout

```bash
# Apply migration
cd src/Services/Order/UrbanX.Order.Persistence
dotnet ef database update
```

Sau khi migration chạy, service tự nhận event Catalog mới. Để bootstrap data
cho variant cũ đã tồn tại trước khi consumer chạy, có 2 cách:

1. **Tự warm up qua L3 fallback**: lần đầu request có variant chưa có trong L2
   → L3 HTTP → cache vào L1. Khi event tiếp theo của variant đó về → L2 populated.
2. **Manual replay**: gọi Catalog `/api/v1/products` để get tất cả, manual insert
   vào `read.catalog_snapshots` (one-off script).

Mặc định approach 1 — dual-mode hoạt động ngay.

## Non-Goals

- KHÔNG đụng Inventory reservation logic
- KHÔNG đụng saga state machine
- KHÔNG remove pricing tolerance 1% / 30 min (business rule giữ nguyên)
- KHÔNG tự động bootstrap historical data (warm-up qua L3 fallback)

## Test / Verification

Build:

```bash
rtk err dotnet build UrbanX.sln
rtk summary dotnet test tests/UrbanX.Services.Order.UnitTests/
```

Behavior tests sau khi deploy:

1. Seed product qua Catalog → kiểm consumer log + row trong `read.catalog_snapshots`
2. POST `/api/v1/orders` — verify `order.validator.source=local_hit` (sau khi consumer xử lý event)
3. Update product price qua Catalog → `ProductVariantUpdatedV1` về → cache invalidated → next order request lấy giá mới
4. Stop Catalog service → POST `/api/v1/orders` — vẫn 202 nếu variant có trong L2 ✅
5. Drop sản phẩm khỏi L2 + Catalog down → POST → 503 với code `CATALOG_UNAVAILABLE`
