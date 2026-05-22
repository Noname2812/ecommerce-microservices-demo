# Shared Libraries — Rules

## Dependency Order

```
Shared.Kernel  (no deps)
  ├── Shared.Cache  (→ Shared.Kernel)
  └── Shared.Contract  (no deps — standalone cross-service contracts)
        └── Shared.Application  (→ Shared.Kernel + Shared.Contract)
              └── Shared.Messaging  (→ Shared.Kernel + Shared.Application + Shared.Cache)
Shared.Observability  (standalone)
```

> **Transactional outbox** dùng MassTransit EF Outbox (`MassTransit.EntityFrameworkCore`) thay vì custom library. DbContext register MT entities + `bus.AddEntityFrameworkOutbox<TDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); ... })`. Handler publish qua `IEventPublisher.PublishAsync(evt, ct)` — MT bus outbox stage vào `outbox_message` rows trong cùng EF transaction.

---

## Shared.Kernel

**Namespace:** `Shared.Kernel`  
**Chứa:** Domain primitives dùng ở mọi tầng — không phụ thuộc NuGet nào.

| Type | File |
|---|---|
| `Error` | `Primitives/Error.cs` |
| `Result`, `Result<T>` | `Primitives/Result.cs`, `ResultT.cs` |
| `DomainException` | `Primitives/DomainException.cs` |
| `PageResult<T>` | `Primitives/PageResult.cs` |
| `IValidationResult`, `ValidationResult`, `ValidationResult<T>` | `Primitives/IValidationResult.cs`, `ValidationResult.cs`, `ValidationResultT.cs` |
| `AuthorizationErrors` | `Primitives/AuthorizationErrors.cs` — `AUTH_REQUIRED`, `FORBIDDEN` errors dùng bởi `AuthorizationPipelineBehavior` |
| `IUnitOfWork` | `Primitives/IUnitOfWork.cs` — abstraction transactional boundary; impl `EfUnitOfWork` đặt trong Persistence của mỗi service |
| `BaseEntity<TKey>` | `Domain/BaseEntity.cs` |
| `IDateTracking`, `ISoftDelete`, `IUserTracking` | `Domain/I*.cs` |
| `GatewayHeaderNames` | `Constants/GatewayHeaderNames.cs` — header name constants shared across Gateway + downstream services |

**Rules:**
- Không thêm NuGet dependency vào project này
- Không import từ bất kỳ Shared.* project nào khác
- Mọi project cần `Error`, `Result`, `DomainException`, `BaseEntity` đều `using Shared.Kernel`

---

## Shared.Contract

**Namespace:** `Shared.Contract.Abstractions` / `Shared.Contract.Dtos.*` / `Shared.Contract.Messaging.*`  
**Chứa:** Contracts dùng qua service boundary — integration events + DTOs.

| Type | Namespace |
|---|---|
| `IIntegrationEvent`, `IntegrationEventBase`, `IIntegrationCommand` | `Shared.Contract.Abstractions` |
| Catalog DTOs (`ProductDtos`) | `Shared.Contract.Dtos.Catalog` |
| Catalog events (`ProductCreated`, `ProductUpdateEvents`) | `Shared.Contract.Messaging.Catalog` |

**Rules:**
- Không chứa domain primitives (`Result`, `Error`, `BaseEntity` v.v.) — những thứ đó thuộc `Shared.Kernel`
- Mỗi service có folder riêng trong `Messaging/<ServiceName>/` và `Dtos/<ServiceName>/`
- Thêm event mới: tạo record kế thừa `IntegrationEventBase`, đặt trong đúng namespace
- Không import `Shared.Application` hay `Shared.Messaging` từ đây

---

## Shared.Application

**Namespace:** `Shared.Application` / `Shared.Application.Authorization`  
**Chứa:** CQRS abstractions + domain event interfaces + authorization contracts. Không chứa implementation.

| Type | Namespace | Mục đích |
|---|---|---|
| `ICommandBase`, `ICommand`, `ICommand<T>` | `Shared.Application` | API-dispatched command markers (MediatR `IRequest`) — kích hoạt **TransactionPipelineBehavior** |
| `ICommandHandler<C>`, `ICommandHandler<C,R>` | `Shared.Application` | Handler interfaces cho `ICommand` |
| `IMessagingCommand`, `IMessagingCommand<T>` | `Shared.Application` | Consumer-dispatched command markers — **SKIP TransactionPipelineBehavior + IdempotencyPipelineBehavior** (vì MT EF Outbox đã quản transaction + InboxState đã dedup) |
| `IMessagingCommandHandler<C>`, `IMessagingCommandHandler<C,R>` | `Shared.Application` | Handler interfaces cho `IMessagingCommand` |
| `IQuery<T>`, `IQueryHandler<Q,R>` | `Shared.Application` | Query interfaces |
| `IDomainEvent`, `DomainEventBase` | `Shared.Application` | In-process domain events |
| `IDomainEventHandler<T>` | `Shared.Application` | Domain event handler |
| `IEventPublisher` | `Shared.Application` | Publish integration events (impl ở Shared.Messaging) |
| `IIdempotentCommand` | `Shared.Application` | Marker cho idempotent commands |
| `IMessageRequestClient` | `Shared.Application` | Service-to-service RPC (impl ở Shared.Messaging) |
| `ISagaState` | `Shared.Application` | Saga state contract (extends MassTransit `SagaStateMachineInstance`) |
| `IUserContext` | `Shared.Application.Authorization` | Identity từ Gateway header (impl `UserHttpContext` ở Shared.Messaging) |
| `PermissionScope` | `Shared.Application.Authorization` | enum `None / Own / All` |
| `Permissions`, `Roles` | `Shared.Application.Authorization` | **Constants** — bắt buộc dùng thay string literal |
| `RequirePermissionAttribute`, `RequireRoleAttribute`, `AllowAnonymousAttribute` | `Shared.Application.Authorization` | Gắn trên Command/Query để khai báo permission required |

**Rules:**
- Chỉ chứa interfaces/abstractions — không có class implementation nào trừ `DomainEventBase` và attributes
- `using Shared.Kernel` cho `Result`, `Error`; `using Shared.Contract.Abstractions` cho `IIntegrationEvent`
- **Authorization:** thêm permission/role mới CHỈ ở `Permissions.cs` / `Roles` static class — không bao giờ hardcode string ở Command/Query/Handler
- Format permission: `"<resource>:<action>"` (vd `product:write`, `inventory:read`)

---

## Shared.Messaging

**Namespace:** `Shared.Messaging` / `Shared.Messaging.Behaviors` / `Shared.Messaging.Saga` / `Shared.Messaging.Authorization`  
**Chứa:** MassTransit + MediatR infrastructure + Trust-Gateway user context.

`<FrameworkReference Include="Microsoft.AspNetCore.App" />` để truy cập `IHttpContextAccessor`, `HttpContext`, middleware.

| Component | File |
|---|---|
| MediatR behaviors | `Behaviors/Validation|Logging|Idempotency|Authorization|DistributedLockPipelineBehavior|TransactionPipelineBehavior.cs` |
| MassTransit setup | `DependencyInjection/Extensions/ServiceCollectionExtensions.cs` |
| Event publisher impl | `EventPublisher.cs` |
| Consumer base (legacy) | `IntegrationEventConsumerBase.cs` — KHÔNG dùng cho consumer mới (xem rule bên dưới) |
| Saga base classes | `Saga/SagaStateBase.cs`, `SagaStateMachineBase.cs`, `CompensatableActivityBase.cs` |
| User context impl | `Authorization/UserHttpContext.cs` (internal — đọc Gateway headers) |
| User context middleware | `Authorization/UserContextMiddleware.cs` (set OpenTelemetry tags) |
| Builder extension | `Authorization/UserContextApplicationBuilderExtensions.cs` (`app.UseUserContext()`) |

**DI entry points:**
- `AddMessaging(...)` — đăng ký MassTransit + RabbitMQ + `IEventPublisher` (không bật `UseMessageRetry`, prefetch, concurrent limit toàn bus; khai báo per-consumer / per-endpoint khi cần)
- `AddMediatorWithPielineDefault(assembly)` — đăng ký MediatR + tất cả behaviors mặc định (Authorization → Idempotency → Validation → DistributedLock → Transaction) + `IHttpContextAccessor` + `IUserContext`
- `AddConfigMessaging(config)` — bind `RabbitMqOptions` từ config

**App pipeline:**
- `app.UseUserContext()` — dùng trước `app.MapCarter()` để enrich tracing tags từ Gateway headers

**Rules:**
- Consumer mới: implement **trực tiếp `IConsumer<TEvent>`** (MassTransit) trong `<Service>.Infrastructure/Messaging/<Event>/`, dispatch qua `ISender` (MediatR). KHÔNG kế thừa `IntegrationEventConsumerBase` — đây là pattern legacy chỉ còn để tương thích ngược. Xem skill `add-consumer` + ref [Inventory](src/Services/Inventory/UrbanX.Inventory.Infrastructure/Messaging/).
- Behavior mới đăng ký trong `AddMediatorWithPielineDefault()`
- `TransactionPipelineBehavior` dùng `IUnitOfWork` — mỗi service phải register `IUnitOfWork` trong `AddPersistence()` với `EfUnitOfWork<TDbContext>` tương ứng
- `AuthorizationPipelineBehavior` tự động short-circuit Command/Query có `[RequirePermission]`/`[RequireRole]` mà không pass check — wrap `AuthorizationErrors` thành `Result.Failure` (hoặc `Result<T>.Failure`)
- `using Shared.Kernel` cho `Result`, `Error`; `using Shared.Application` cho CQRS interfaces; `using Shared.Application.Authorization` cho `IUserContext`

---

## Shared.Cache

**Namespace:** `Shared.Cache` / `Shared.Cache.Abstractions` / `Shared.Cache.Attributes`  
**Chứa:** Redis cache, distributed lock, Lua script execution. Không phụ thuộc Shared.Application hay Shared.Messaging.

| Type | File | Mục đích |
|---|---|---|
| `ICacheService` | `Abstractions/ICacheService.cs` | Cache ops + Lua eval |
| `IDistributedLockService` | `Abstractions/IDistributedLockService.cs` | Distributed lock |
| `ILockHandle` | `Abstractions/IDistributedLockService.cs` | `IAsyncDisposable` — auto-release khi `await using` |
| `DistributedLockAttribute` | `Attributes/DistributedLockAttribute.cs` | Gắn trên Command/Query để khai báo lock |
| `CacheErrors` | `CacheErrors.cs` | `LockTimeout(resource)` error |
| `CacheOptions` | `DependencyInjection/Options/CacheOptions.cs` | `SectionName = "Shared:Cache"` |

**DI entry point:** `builder.AddSharedCache("redis")` — đăng ký `IConnectionMultiplexer` (Aspire), `IDistributedCache` (Redis), `ICacheService`, `IDistributedLockService`.

**AppHost:** mỗi service dùng cache phải có `.WithReference(redis).WaitFor(redis)` trong `AppHost.cs`.

**`DistributedLockPipelineBehavior`** (trong Shared.Messaging):
- Reflect `[DistributedLock]` trên TRequest
- Template `{PropertyName}` → replace bằng giá trị property thực
- Acquire lock → handler chạy → auto-release
- Timeout → `Result.Failure(CacheErrors.LockTimeout(resource))`

**Key format:** `{InstanceName}:{key}` — prefix đến từ `CacheOptions.InstanceName` (default `"urbanx"`).  
**Lock key format:** `{InstanceName}:lock:{resource}`

**Rules:**
- Thêm service mới dùng cache: (1) thêm `ProjectReference` Shared.Cache vào `.csproj`, (2) thêm `.WithReference(redis)` trong AppHost, (3) `builder.AddSharedCache("redis")` trong `Program.cs`
- `RemoveByPatternAsync` dùng `SCAN` — safe cho Redis Cluster (không dùng `KEYS`)
- `IDistributedCache` được register tự động — đừng register lại thủ công
- `IDistributedLockService` → Singleton (IConnectionMultiplexer thread-safe)
- Doc: `docs/shared/shared-cache.md`

---

## Transactional Outbox — MassTransit EF Outbox

UrbanX không có Shared.Outbox riêng. Outbox infrastructure đến từ package `MassTransit.EntityFrameworkCore` (đã transitively reference qua `Shared.Messaging`).

**Wiring per service:**

**1) `*DbContext.cs` — register MT entities trong `OnModelCreating`:**
```csharp
using MassTransit;

protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);

    builder.AddInboxStateEntity();    // inbox_state — consumer dedup
    builder.AddOutboxMessageEntity(); // outbox_message — staged publishes
    builder.AddOutboxStateEntity();   // outbox_state — delivery state

    builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
}
```

**2) `Program.cs` — register EF Outbox + Bus Outbox trong `AddMessaging`:**
```csharp
builder.Services
    .AddConfigMessaging(builder.Configuration)
    .AddMessaging(builder.Configuration, configureBus: bus =>
    {
        bus.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();                            // intercept IPublishEndpoint publishes
            o.QueryDelay              = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
        });

        // consumers, sagas, etc.
    });
```

**3) Handler — publish qua `IEventPublisher`:**
```csharp
public sealed class MyCommandHandler(IRepo repo, IEventPublisher eventPublisher)
    : ICommandHandler<MyCommand>
{
    public async Task<Result> Handle(MyCommand cmd, CancellationToken ct)
    {
        var entity = new Entity(...);
        await repo.AddAsync(entity, ct);

        await eventPublisher.PublishAsync(new MyEventV1(entity.Id, ...), ct);

        return Result.Success();
    }
}
```

**Rules:**
- DbContext **bắt buộc** call 3 `Add*Entity()` của MT trong `OnModelCreating` (nếu không sẽ thiếu tables khi migration scaffolding)
- Persistence csproj cần `<PackageReference Include="EFCore.NamingConventions" />` (cho snake_case) và reference `Shared.Messaging` (cho MassTransit namespace)
- Handler **không** inject `IPublishEndpoint` trực tiếp — dùng `IEventPublisher` (impl wrap publish endpoint + thêm logging/MessageId/correlation headers)
- `TransactionPipelineBehavior` (Shared.Messaging) đã wrap handler trong EF transaction qua `IUnitOfWork.ExecuteInTransactionAsync` — MT bus outbox tự enlist vào transaction đó nhờ SaveChanges interceptor
- `BusOutboxDeliveryService` (MT auto-register IHostedService) poll `outbox_message` rồi publish lên RabbitMQ; at-least-once guarantee + MessageId dedup trong `DuplicateDetectionWindow`
- Order service đã có sẵn cấu hình tham chiếu: [OrderDbContext.cs](src/Services/Order/UrbanX.Order.Persistence/OrderDbContext.cs) · [Program.cs](src/Services/Order/UrbanX.Order.API/Program.cs)

---

## Shared.Observability

**Namespace:** `Shared.Observability`  
**Chứa:** OpenTelemetry setup — metrics, tracing, OTLP exporter.

**DI:** `AddObservability(config)`.

**Rules:**
- Không thêm business logic vào đây
- Chỉnh `TracingOptions` trong `appsettings.json` để cấu hình endpoint/sampling
