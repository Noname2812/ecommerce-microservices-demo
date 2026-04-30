# Shared Libraries — Rules

## Dependency Order

```
Shared.Kernel  (no deps)
  ├── Shared.Cache  (→ Shared.Kernel)
  └── Shared.Contract  (no deps — standalone cross-service contracts)
        └── Shared.Application  (→ Shared.Kernel + Shared.Contract)
              └── Shared.Messaging  (→ Shared.Kernel + Shared.Application + Shared.Cache)
Shared.Outbox  (→ Shared.Contract)
Shared.Observability  (standalone)
```

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
| `ICommand`, `ICommand<T>` | `Shared.Application` | Command markers (MediatR `IRequest`) |
| `ICommandHandler<C>`, `ICommandHandler<C,R>` | `Shared.Application` | Handler interfaces |
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
| Consumer base | `IntegrationEventConsumerBase.cs` |
| Saga base classes | `Saga/SagaStateBase.cs`, `SagaStateMachineBase.cs`, `CompensatableActivityBase.cs` |
| User context impl | `Authorization/UserHttpContext.cs` (internal — đọc Gateway headers) |
| User context middleware | `Authorization/UserContextMiddleware.cs` (set OpenTelemetry tags) |
| Builder extension | `Authorization/UserContextApplicationBuilderExtensions.cs` (`app.UseUserContext()`) |

**DI entry points:**
- `AddMessaging(...)` — đăng ký MassTransit + RabbitMQ + `IEventPublisher`
- `AddMediatorWithPielineDefault(assembly)` — đăng ký MediatR + tất cả behaviors mặc định (Authorization → Idempotency → Validation → DistributedLock → Transaction) + `IHttpContextAccessor` + `IUserContext`
- `AddConfigMessaging(config)` — bind `RabbitMqOptions` từ config

**App pipeline:**
- `app.UseUserContext()` — dùng trước `app.MapCarter()` để enrich tracing tags từ Gateway headers

**Rules:**
- Consumer mới kế thừa `IntegrationEventConsumerBase<TEvent, TConsumer>`
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

## Shared.Outbox

**Namespace:** `Shared.Outbox` / `Shared.Outbox.Abstractions`  
**Chứa:** Transactional outbox pattern — EF Core + background relay worker.

| Type | Mục đích |
|---|---|
| `IOutboxWriter` | Inject vào command handlers để ghi event |
| `IOutboxRepository` | EF Core CRUD cho outbox table |
| `OutboxMessage` | Entity ánh xạ bảng `outbox_messages` |
| `OutboxRelayWorker` | `IHostedService` poll + publish lên RabbitMQ |

**DI:** `AddOutbox<TDbContext>()` — đăng ký tất cả services + hosted worker.

**Rules:**
- Service dùng outbox: DbContext phải kế thừa hoặc include `OutboxMessage` entity
- Chỉ dùng `IOutboxWriter` trong command handlers — không publish trực tiếp qua `IEventPublisher` khi cần at-least-once guarantee

---

## Shared.Observability

**Namespace:** `Shared.Observability`  
**Chứa:** OpenTelemetry setup — metrics, tracing, OTLP exporter.

**DI:** `AddObservability(config)`.

**Rules:**
- Không thêm business logic vào đây
- Chỉnh `TracingOptions` trong `appsettings.json` để cấu hình endpoint/sampling
