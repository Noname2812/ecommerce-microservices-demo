# Inventory Service

.NET 10 — Clean Architecture, Carter, MediatR (CQRS), EF Core + PostgreSQL, Transactional Outbox.

Port: **dynamic (Aspire)** | DB: `urbanx_inventory` | Connection string: `inventorydb` | Status: **Active**

---

## Projects

| Project | Responsibility |
|---|---|
| `UrbanX.Inventory.Domain` | Entities, value objects, domain exceptions, repository interfaces |
| `UrbanX.Inventory.Application` | Commands, handlers, validators, error codes, MediatR behavior |
| `UrbanX.Inventory.Persistence` | EF Core DbContext, entity configs, repos, migrations |
| `UrbanX.Inventory.API` | Carter modules, HTTP endpoints, Trust-Gateway user context middleware, OpenAPI, Program.cs |
| `UrbanX.Inventory.Infrastructure` | Empty placeholder |

**Dependency order:** Domain ← Persistence ← Application ← API

---

## Domain

### Entities

**Warehouse** — kho hàng vật lý
- `Guid Id`, `string Name`, `string Code` (unique), `bool IsActive`
- `WarehouseAddress Address` — value object lưu JSONB
- Navigation: `ICollection<InventoryItem> InventoryItems`

**InventoryItem** — aggregate chính; nguồn sự thật duy nhất về stock, per variant × warehouse
- Denormalized từ Catalog (không có FK cross-service): `ProductId`, `ProductName`, `VariantId`, `VariantSku`, `VariantName?`
- `Guid? WarehouseId` — FK → `warehouses`
- `int QuantityOnHand`, `int QuantityReserved`
- `int QuantityAvailable` — **computed column** `quantity_on_hand - quantity_reserved` (read-only, `private set`)
- `int ReorderPoint` (default 10), `int ReorderQuantity` (default 50)
- `DateTimeOffset UpdatedAt`
- Unique constraint: `(VariantId, WarehouseId)`
- Navigation: `Warehouse?`, `ICollection<InventoryReservation> Reservations`, `ICollection<StockMovement> Movements`

**InventoryReservation** — giữ hàng khi order được tạo
- `Guid InventoryItemId` → FK cascade, `Guid OrderId`, `Guid OrderItemId`
- `int Quantity`, `string Status` (`ReservationStatus`), `DateTimeOffset ExpiresAt`
- `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`

**StockMovement** — append-only audit trail, không bao giờ xóa
- `Guid InventoryItemId` → FK restrict, `string MovementType` (`MovementType`)
- `int QuantityChange` (Positive = nhập, Negative = xuất), `int QuantityBefore`, `int QuantityAfter`
- `string? ReferenceType`, `Guid? ReferenceId`, `string? Note`
- `Guid? CreatedById`, `string? CreatedByName` — cross-service, không có FK
- `DateTimeOffset CreatedAt`

### Value Objects

| Type | Kind | Values |
|---|---|---|
| `WarehouseAddress` | `record` | `Street?, Ward?, District?, City?, Province?, Country?, ZipCode?` |
| `ReservationStatus` | `static class` | `Reserved, Confirmed, Released, Cancelled` |
| `MovementType` | `static class` | `Receipt, Sale, Return, Adjustment, TransferIn, TransferOut, Reservation, Release` |

### Repository Interfaces (trong Domain project)

**`IWarehouseRepository`**, **`IInventoryItemRepository`**, **`IInventoryReservationRepository`**, **`IStockMovementRepository`** — hiện tại empty; methods thêm theo từng use case.

---

## Application

### MediatR Behavior

`TransactionPipelineBehavior` (từ `Shared.Messaging`) — wraps mọi command trong DB transaction qua `IUnitOfWork` (impl: `EfUnitOfWork` trong Persistence). Behaviors registered mặc định bởi `AddMediatorWithPielineDefault`: Authorization → Idempotency → Validation → DistributedLock → Transaction.

### Error Codes (`InventoryErrors.cs`)

Chưa có. Thêm theo pattern:

```csharp
public static Error NotFound(Guid id) =>
    new("InventoryItem.NotFound", $"Inventory item {id} not found");
```

---

## Persistence

### `InventoryDbContext`

Kế thừa `OutboxDbContext` (từ `Shared.Outbox`). DbSets:

`Warehouses`, `InventoryItems`, `InventoryReservations`, `StockMovements` + `OutboxMessages` (inherited)

### Table Names

| Entity | Table |
|---|---|
| Warehouse | `warehouses` |
| InventoryItem | `inventory_items` |
| InventoryReservation | `inventory_reservations` |
| StockMovement | `stock_movements` |

### Notable EF Config

- `InventoryItem.QuantityAvailable` — `HasComputedColumnSql("quantity_on_hand - quantity_reserved", stored: true)`
- `Warehouse.Address` — `HasColumnType("jsonb")` với `System.Text.Json` ValueConverter
- `InventoryItem.(VariantId, WarehouseId)` — composite unique index
- `StockMovement.(InventoryItemId, CreatedAt)` — index cho audit query
- `InventoryReservation.(Status, ExpiresAt)` — index cho expiry sweep
- Tất cả PKs: `ValueGeneratedNever()` (app assigns GUID)

### Repositories

`WarehouseRepository`, `InventoryItemRepository`, `InventoryReservationRepository`, `StockMovementRepository` — sealed, hiện tại empty.

### Migrations

Pending: `InitialCreate` (chưa chạy — xem `docs/inventory/migrations/initial-schema.md`)

```bash
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add InitialCreate
```

### Design-Time Factory

`InventoryDbContextFactory` đọc env var `ConnectionStrings__inventorydb`.  
Fallback: `Host=localhost;Port=5432;Database=urbanx_inventory;Username=postgres;Password=postgres`

---

## API

### Endpoints (`Apis/InventoryItemApis.cs`)

Base: `/api/v{version:apiVersion}/inventory/items`

Chưa có endpoint. Thêm bằng skill `add-command` / `add-query`.

### `ApiEndpoint` base class

`ToInventoryResult(Result)` / `ToInventoryResult<T>(Result<T>)` — maps error codes → HTTP status:
- `*NotFound` → 404
- `FORBIDDEN` → 403
- `OPTIMISTIC_LOCK_CONFLICT` → 409
- default → 400

### Authentication / Authorization

**Trust-the-Gateway pattern.** Service KHÔNG verify JWT — Gateway verify, enrich `X-User-*` headers, strip Authorization trước khi forward.

- `app.UseUserContext()` middleware đọc headers, set OpenTelemetry activity tags
- `IUserContext` (scoped) đọc identity từ headers per request
- Endpoints **KHÔNG** dùng `RequireAuthorization()` — authorization qua MediatR `AuthorizationPipelineBehavior`
- Command/Query gắn `[RequirePermission(Permissions.Inventory.*)]` để khai báo permission required
- Xem `docs/auth/trust-gateway-flow.md`

### Program.cs Registration Order

```
AddServiceDefaults() → AddOpenApi() → AddNpgsqlDbContext<InventoryDbContext>("inventorydb")
→ AddOutbox<InventoryDbContext>() → AddApplication()  // options cho ConsumerDefinition, MediatR, …
→ AddConfigMessaging() → AddMessaging() → AddPersistence() → Carter

app.UseExceptionHandler() → app.UseUserContext() → app.MapCarter()
```

Auto-runs EF migrations on startup.

---

## Gateway Route

Defined in `src/Gateway/UrbanX.Gateway/appsettings.json`:

- Route: `inventory-route` → `/api/v1/inventory/{**catch-all}`
- Cluster: `inventory-cluster` → `http://localhost:5006` (overridden by Aspire at runtime)

---

## AppHost Registration

```csharp
var inventoryDb = postgres.AddDatabase("inventorydb", "urbanx_inventory");

var inventoryService = builder.AddProject<Projects.UrbanX_Inventory_API>("inventory")
    .WithReference(inventoryDb)
    .WithReference(rabbitMq)
    .WaitFor(inventoryDb)
    .WaitFor(rabbitMq);

gateway
    .WithReference(inventoryService)
    .WaitFor(inventoryService);
```

---

## Integration Events / MassTransit

- Contracts: `Shared.Contract/Messaging/PlaceOrder/` (ví dụ `InventoryReleaseRequestedV1`, `IInventoryReleaseRequested`).
- Consumer compensation: `InventoryReleaseRequestedConsumer` + `InventoryReleaseRequestedConsumerDefinition` — bind queue tới fanout exchange `compensation.events`. **`InventoryReleaseRequestedProcessor`** chỉ gọi `ReleaseInventoryCommand` (không gọi `ExistsAsync` — tránh double read; dedupe nằm ở `ReleaseInventoryCommandHandler` + `StageInsert` + unique `EventId`). Lỗi `Result` từ handler → `InventoryReleaseCommandFailedException` (consumer `IsTransient` = true) để retry MassTransit ghi **Warning**, không *Fatal* mỗi lần. Retry / queue / throughput: **`InventoryReleaseRequestedConsumerOptions`**, validate startup qua **`InventoryReleaseRequestedConsumerOptionsValidator`**.

### RabbitMQ consumer tuning (retry & throughput, opt-in)

`AddMessaging` **không** đăng ký `UseMessageRetry` toàn bus và **không** set `PrefetchCount` / `ConcurrentMessageLimit` toàn bus.

- **Compensation consumer:** cấu hình cục bộ qua `InventoryReleaseRequestedConsumerOptions` — `QueueName`, `Retry` (`Intervals`, `IntervalSeconds`; đặt một trong hai = 0 để **tắt** broker retry), `PrefetchCount` / `ConcurrentMessageLimit` (chỉ áp dụng khi có giá trị > 0).
- **Consumer khác:** khai báo trong `ConsumerDefinition.ConfigureConsumer` và/hoặc class options riêng (inject `IOptions<YourConsumerOptions>` trong ctor definition).

## Key Patterns

**Transactional Outbox** — Inject `IOutboxWriter` trong command handlers. `OutboxRelayWorker` publish lên RabbitMQ.

**Saga Choreography (planned)** — Inventory tham gia order saga: nhận `OrderCreated`, kiểm tra/reserve stock, emit `StockReserved` hoặc `StockReservationFailed`.
