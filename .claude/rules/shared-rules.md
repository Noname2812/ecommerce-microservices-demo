# Shared Libraries — Rules

## Dependency Order

```
Shared.Kernel  (no deps)
  └── Shared.Contract  (no deps — standalone cross-service contracts)
        └── Shared.Application  (→ Shared.Kernel + Shared.Contract)
              └── Shared.Messaging  (→ Shared.Kernel + Shared.Application)
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

**Namespace:** `Shared.Application`  
**Chứa:** CQRS abstractions + domain event interfaces. Không chứa implementation.

| Type | Mục đích |
|---|---|
| `ICommand`, `ICommand<T>` | Command markers (MediatR `IRequest`) |
| `ICommandHandler<C>`, `ICommandHandler<C,R>` | Handler interfaces |
| `IQuery<T>`, `IQueryHandler<Q,R>` | Query interfaces |
| `IDomainEvent`, `DomainEventBase` | In-process domain events |
| `IDomainEventHandler<T>` | Domain event handler |
| `IEventPublisher` | Publish integration events (impl ở Shared.Messaging) |
| `IIdempotentCommand` | Marker cho idempotent commands |
| `IMessageRequestClient` | Service-to-service RPC (impl ở Shared.Messaging) |
| `ISagaState` | Saga state contract (extends MassTransit `SagaStateMachineInstance`) |

**Rules:**
- Chỉ chứa interfaces/abstractions — không có class implementation nào trừ `DomainEventBase`
- `using Shared.Kernel` cho `Result`, `Error`; `using Shared.Contract.Abstractions` cho `IIntegrationEvent`

---

## Shared.Messaging

**Namespace:** `Shared.Messaging` / `Shared.Messaging.Behaviors` / `Shared.Messaging.Saga`  
**Chứa:** MassTransit + MediatR infrastructure.

| Component | File |
|---|---|
| MediatR behaviors | `Behaviors/Validation|Logging|Idempotency|TransactionPipelineBehavior.cs` |
| MassTransit setup | `DependencyInjection/Extensions/ServiceCollectionExtensions.cs` |
| Event publisher impl | `EventPublisher.cs` |
| Consumer base | `IntegrationEventConsumerBase.cs` |
| Saga base classes | `Saga/SagaStateBase.cs`, `SagaStateMachineBase.cs`, `CompensatableActivityBase.cs` |

**DI entry points:**
- `AddMessaging(...)` — đăng ký MassTransit + RabbitMQ + `IEventPublisher`
- `AddMediator(...)` — đăng ký MediatR + tất cả behaviors
- `AddConfigMessaging(config)` — bind `RabbitMqOptions` từ config

**Rules:**
- Consumer mới kế thừa `IntegrationEventConsumerBase<TEvent, TConsumer>`
- Behavior mới đăng ký trong `AddMediator()`
- `using Shared.Kernel` cho `Result`, `Error`; `using Shared.Application` cho CQRS interfaces

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
