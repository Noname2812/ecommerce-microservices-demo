# TASK-03 — Cleanup Catalog Snapshot System

**Team:** Order · **Effort:** L (2d) · **Depends:** —
**Branch:** `feature/order-refactor/TASK-03-cleanup-catalog-snapshot`

## Mục đích

Xoá toàn bộ Catalog read-model snapshot trong Order service (model, persistence, projection consumers, cache, ProcessedEvent, telemetry, configs, validators dùng snapshot). Saga sau này gọi thẳng Catalog HTTP qua `ICatalogServiceClient` (TASK-05).

## Files to DELETE (25 files + folders)

### Domain
- `src/Services/Order/UrbanX.Order.Domain/ReadModels/CatalogSnapshot.cs`
- `src/Services/Order/UrbanX.Order.Domain/Models/ProcessedEvent.cs`
- `src/Services/Order/UrbanX.Order.Domain/Repositories/IProcessedEventRepository.cs`
- Folder `ReadModels/` (sau khi rỗng)

### Application
- `src/Services/Order/UrbanX.Order.Application/ReadModels/CatalogSnapshotRow.cs`
- `src/Services/Order/UrbanX.Order.Application/ReadModels/ICatalogSnapshotReader.cs`
- `src/Services/Order/UrbanX.Order.Application/ReadModels/ICatalogSnapshotWriter.cs`
- Folder `ReadModels/` (sau khi rỗng)
- `src/Services/Order/UrbanX.Order.Application/Messaging/Catalog/` — TOÀN BỘ folder (7 files):
  - `CatalogProjectionConsumerBase.cs`
  - `ProductCreatedConsumer.cs`
  - `ProductInfoUpdatedConsumer.cs`
  - `ProductStatusChangedConsumer.cs`
  - `ProductVariantAddedConsumer.cs`
  - `ProductVariantUpdatedConsumer.cs`
  - `ProductVariantDeletedConsumer.cs`
- `src/Services/Order/UrbanX.Order.Application/Constants/CatalogProjectionConstants.cs`
- `src/Services/Order/UrbanX.Order.Application/Constants/SaleProjectionConstants.cs`
- `src/Services/Order/UrbanX.Order.Application/Abstractions/Catalog/IProductSnapshotCache.cs`
- `src/Services/Order/UrbanX.Order.Application/Abstractions/Promotion/ISaleSnapshotCache.cs`
- `src/Services/Order/UrbanX.Order.Application/Abstractions/ISaleAllocationGate.cs`
- `src/Services/Order/UrbanX.Order.Application/Telemetry/OrderValidatorMetrics.cs`
- Folder `Telemetry/` (sau khi rỗng)

### Application Validators (DELETE — logic chuyển inline vào saga ở TASK-07/08)
- `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderBusinessValidatorsImpl.cs`
- `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/SalePricingValidator.cs`
- `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/SaleEligibilityValidator.cs`

### Infrastructure
- `src/Services/Order/UrbanX.Order.Infrastructure/Services/RedisProductSnapshotCache.cs`
- `src/Services/Order/UrbanX.Order.Infrastructure/Services/MemoryRedisSaleSnapshotCache.cs`
- `src/Services/Order/UrbanX.Order.Infrastructure/Services/SaleAllocationGate.cs`
- `src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Options/CatalogSnapshotOptions.cs`

### Persistence
- `src/Services/Order/UrbanX.Order.Persistence/Configurations/Read/CatalogSnapshotConfiguration.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Configurations/ProcessedEventConfiguration.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Repositories/Read/DapperCatalogSnapshotReader.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Repositories/Read/DapperCatalogSnapshotWriter.cs`
- `src/Services/Order/UrbanX.Order.Persistence/Repositories/ProcessedEventRepository.cs`
- Folder `Configurations/Read/`, `Repositories/Read/` (sau khi rỗng)

## Files to MODIFY

### `Order.Persistence/OrderDbContext.cs`
- Bỏ `DbSet<CatalogSnapshot>`, `DbSet<ProcessedEvent>`
- (Phase 7 sẽ đổi base class — chưa touch trong task này, chỉ remove DbSet)

### `Order.Persistence/Constants/TableNames.cs`
- Bỏ entries: `catalog_snapshots`, `processed_events`

### `Order.Persistence/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`
- Bỏ register `ICatalogSnapshotReader`, `ICatalogSnapshotWriter`, `IProcessedEventRepository`

### `Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`
- Bỏ register `IProductSnapshotCache`, `ISaleSnapshotCache`, `ISaleAllocationGate`
- Bỏ bind `CatalogSnapshotOptions`, `SaleSnapshotOptions`
- KEEP `HttpClient<ICatalogServiceClient>` (TASK-05 extend)

### `Order.API/Program.cs`
- Bỏ 6 dòng `bus.AddConsumer<ProductXxxConsumer>()` (lines ~59-64)
- Bỏ `metrics.AddMeter(CatalogProjectionConstants.Metrics.MeterName)` (lines ~25-26)
- Cleanup `using` directives

### `Order.Application.csproj`
- Bỏ `<PackageReference Include="Dapper" />` (nếu chỉ dùng cho deleted Dapper readers)

### `Order.Persistence.csproj`
- Bỏ `<PackageReference Include="Dapper" />`

### `Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs`
- Bỏ inject `ISaleAllocationGate`, `ISaleSnapshotCache`, `ICatalogSnapshotReader` — handler sẽ rewrite hoàn toàn ở TASK-06; trong task này chỉ stub bỏ inject để build pass.

## Acceptance Criteria

- [ ] Build solution OK (TASK-06/07/08 chưa làm, handlers/sagas có thể còn reference inject cũ — comment-out hoặc stub bỏ để build pass)
- [ ] Không còn file nào trong các folder đã delete
- [ ] No new warning
- [ ] Grep `ICatalogSnapshotReader|IProductSnapshotCache|ISaleSnapshotCache|ProcessedEvent|CatalogProjection` ở folder `Order` không còn match (trừ migration cũ)

## Notes

- **Migration KHÔNG generate trong task này** — đợi TASK-12 tổng hợp 1 migration
- Stub bỏ inject để build pass: tạm thời comment-out call site, để TASK-06/07/08 rewrite hoàn toàn
- `SaleAllocationGate` flash sale quota → chuyển sang Promotion service xử lý qua `RedeemSalePromotionRequestedV1` (saga publish, Promotion check + reserve quota atomic)

## DoD

- [ ] Files deleted committed
- [ ] Build pass (có thể với stub)
- [ ] Notify Order team unblock TASK-06, 07, 08, 11
