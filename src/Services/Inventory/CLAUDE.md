# Inventory Service

.NET 10 — Clean Architecture, Carter, MediatR (CQRS), EF Core + PostgreSQL, Transactional Outbox.

Port: **dynamic (Aspire)** | DB: `urbanx_inventory` | Connection string: `inventorydb` | Status: **Active (scaffold)**

---

## Projects

| Project | Responsibility |
|---|---|
| `UrbanX.Inventory.Domain` | Entities, value objects, domain exceptions, repository interfaces |
| `UrbanX.Inventory.Application` | Commands, handlers, validators, error codes, MediatR behavior |
| `UrbanX.Inventory.Persistence` | EF Core DbContext, entity configs, repos, migrations |
| `UrbanX.Inventory.API` | Carter modules, HTTP endpoints, JWT auth, OpenAPI, Program.cs |
| `UrbanX.Inventory.Infrastructure` | Empty placeholder |

**Dependency order:** Domain ← Persistence ← Application ← API

---

## Domain

Chưa có entity. Dùng skill `migration-generator` để tạo Domain model + EF config + migration.

Placeholder folders:
- `Domain/Models/`
- `Domain/ValueObjects/`
- `Domain/Exceptions/`

---

## Application

### MediatR Behavior

`InventoryTransactionBehavior<TRequest, TResponse>` — extends `TransactionPipelineBehavior<..., InventoryDbContext>`. Wraps mọi command trong DB transaction.

### Error Codes (`InventoryErrors.cs`)

Chưa có. Thêm theo pattern:

```csharp
public static Error NotFound(Guid id) =>
    new("InventoryItem.NotFound", $"Inventory item {id} not found");
```

---

## Persistence

### `InventoryDbContext`

Kế thừa `OutboxDbContext` (từ `Shared.Outbox`). DbSets sẽ được thêm theo entity.

### Design-Time Factory

`InventoryDbContextFactory` đọc env var `ConnectionStrings__inventorydb`.  
Fallback: `Host=localhost;Port=5432;Database=urbanx_inventory;Username=postgres;Password=postgres`

### Migrations

```bash
cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add <MigrationName>
```

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

### Program.cs Registration Order

```
AddServiceDefaults() → AddOpenApi() → AddNpgsqlDbContext<InventoryDbContext>("inventorydb")
→ AddOutbox<InventoryDbContext>() → AddConfigMessaging() → AddMessaging()
→ AddAuthentication(JwtBearer) → AddApplication() → Carter
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

## Integration Events

Sẽ được định nghĩa trong `Shared.Contract/Messaging/Inventory/` khi implement các use case.  
Consumer kế thừa `IntegrationEventConsumerBase<TEvent, TConsumer>` từ `Shared.Messaging`.

## Key Patterns

**Transactional Outbox** — Inject `IOutboxWriter` trong command handlers. `OutboxRelayWorker` publish lên RabbitMQ.

**Saga Choreography (planned)** — Inventory tham gia order saga: nhận `OrderCreated`, kiểm tra/reserve stock, emit `StockReserved` hoặc `StockReservationFailed`.
