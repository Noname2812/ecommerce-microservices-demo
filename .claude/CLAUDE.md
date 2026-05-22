# Project: UrbanX

.NET 10 microservices e-commerce — Clean Architecture, Carter, MediatR (CQRS), MassTransit + RabbitMQ, EF Core, Transactional Outbox, .NET Aspire.

---

## Service Map

| Service | Port | Status | DB | Notes |
|---|---|---|---|---|
| **Catalog** | 5025 | Active | PostgreSQL (`urbanx_catalog`) | CQRS + MT EF Outbox; read schema (`read.*`) via Dapper |
| **Gateway** | 5000 | Active | — | YARP + Duende.BFF (cookie session) + Rate limiting |
| **Identity** | 5005 | Active | PostgreSQL (`urbanx_identity`) | Duende IdentityServer + ASP.NET Identity + MT EF Outbox |
| **Order** | 5010 | Disabled | PostgreSQL | Saga choreography (MT EF Outbox) |
| **Payment** | 5015 | Disabled | PostgreSQL | Stripe + MT EF Outbox |
| **Merchant** | 5030 | Disabled | PostgreSQL | — |
| **Inventory** | 5020 | Active | PostgreSQL (`urbanx_inventory`) | ⭐ Reference layout (Application + Infrastructure tách rõ). CQRS + MT EF Outbox; 4 entities: Warehouse, InventoryItem, Reservation, StockMovement; Hangfire TTL job |
| **Frontend** | 5173 | Disabled | — | React 19 + Vite |

Infrastructure (Aspire tự quản lý): PostgreSQL, RabbitMQ, Redis.

---

## Cấu trúc thư mục

```
src/
├── AppHost/UrbanX.AppHost/         # Aspire orchestration (AppHost.cs)
├── ServiceDefaults/                # OpenTelemetry, health checks, service discovery
├── Services/
│   ├── Catalog/
│   │   ├── UrbanX.Catalog.API/             # Carter modules (ProductApis.cs)
│   │   ├── UrbanX.Catalog.Application/     # CQRS: Usecases/V1/Command|Query/
│   │   ├── UrbanX.Catalog.Domain/          # Models, ValueObjects, Repositories (interfaces)
│   │   ├── UrbanX.Catalog.Infrastructure/  # (trống, dùng Persistence trực tiếp)
│   │   └── UrbanX.Catalog.Persistence/     # DbContext, Repos, Migrations, SeedData
│   ├── Inventory/                            # ⭐ Reference pattern cho service mới
│   │   ├── UrbanX.Inventory.API/             # Carter modules + Program.cs (Hangfire wiring)
│   │   ├── UrbanX.Inventory.Application/     # CQRS only: Usecases/V1/Command|Query + Validators
│   │   │                                     #   - AddApplication() = AddMediatorWithPielineDefault(...) (1 dòng)
│   │   ├── UrbanX.Inventory.Domain/          # Models, ValueObjects, Errors, Repositories (interfaces)
│   │   ├── UrbanX.Inventory.Infrastructure/  # ⭐ Impl tầng outbound:
│   │   │     Messaging/<Event>/              #   - Consumer (IConsumer<T>) + ConsumerDefinition
│   │   │     Jobs/                           #   - Hangfire recurring jobs
│   │   │     DependencyInjection/Options/    #   - IOptions classes + IValidateOptions validators
│   │   │     DependencyInjection/Extensions/ #   - AddInfrastructure() registers options/jobs/clients
│   │   └── UrbanX.Inventory.Persistence/     # DbContext, Repos, EfUnitOfWork, Migrations
│   └── Identity/
│       ├── UrbanX.Identity.API/              # Carter (AccountApis, ProfileApis, UsersApis, RolesApis, AuditApis) + Duende IdentityServer + Google OAuth
│       ├── UrbanX.Identity.Application/      # CQRS: Register/ConfirmEmail/ForgotPassword/ResetPassword/ChangePassword/UpdateProfile/AssignRole/RevokeRole/Deactivate/Activate
│       ├── UrbanX.Identity.Domain/           # ApplicationUser/Role, UserProfile, AuthAuditLog, AuthEventType
│       ├── UrbanX.Identity.Infrastructure/   # IEmailSender (LogEmailSender), IIdentityAuditWriter
│       └── UrbanX.Identity.Persistence/      # IdentityDbContext (registers MT inbox/outbox entities), Configurations, AuthAuditLogRepository
├── Gateway/
│   ├── UrbanX.Gateway/                     # Program.cs, appsettings (routes, rate limits)
│   ├── UrbanX.Gateway.Application/         # Abstractions, config options
│   └── UrbanX.Gateway.Infrastructure/      # RBAC, enrichment, TLS, observability
└── Shared/
    ├── Shared.Kernel/          # Domain primitives: Error, Result<T>, DomainException, BaseEntity, IDateTracking, ISoftDelete, IUserTracking, IValidationResult, PageResult<T>; GatewayHeaderNames
    ├── Shared.Contract/        # Cross-service contracts: IIntegrationEvent, IntegrationEventBase, integration event DTOs (Catalog)
    ├── Shared.Application/     # CQRS abstractions: ICommand, IQuery, handlers, IDomainEvent, IEventPublisher, ISagaState
    ├── Shared.Messaging/       # MassTransit + RabbitMQ (retry/throughput opt-in per consumer), MediatR pipeline behaviors, saga base, EventPublisher impl
    ├── Shared.Cache/           # Redis cache (ICacheService, IDistributedLockService, [DistributedLock], IDistributedCache); DI: builder.AddSharedCache("redis")
    └── Shared.Observability/   # OpenTelemetry wiring

docs/                           # Docs tổng quan (migrations, security, deployment)
tests/
├── UrbanX.Services.Catalog.UnitTests/      # xUnit + Moq, no DB
└── UrbanX.Services.Catalog.IntegrationTests/ # WebApplicationFactory + EF InMemory
```

---

## Key Patterns

> Architecture rules (layer structure, CQRS, EF, Carter, Authorization): `@.claude/rules/achitecture.md`
> Shared library APIs và rules: `@.claude/rules/shared-rules.md`

### Trust-the-Gateway Auth
- Gateway verify JWT → enrich `X-User-Id` / `X-User-Roles` / `X-Merchant-Id` / `X-Permission-Scope` headers, strip Authorization
- Service KHÔNG verify JWT, KHÔNG `RequireAuthorization()` ở endpoints (Identity là exception)
- `app.UseUserContext()` + `IUserContext` đọc identity từ headers; `AuthorizationPipelineBehavior` enforce
- Testing: gọi service trực tiếp với mock `X-User-*` headers (JWT bearer không đi qua Gateway)
- Doc: `docs/auth/trust-gateway-flow.md` · `docs/gateway/bff.md`

### Identity Service
- Issuer JWT tại port 5005; permission claims gắn trên Role (claim type `permission`)
- `LogEmailSender` (dev) ghi log thay gửi email
- Account lockout: 5 fails → 15 phút; Audit log: bảng `auth_audit_logs`
- Doc: `docs/identity/`

### Distributed Cache
- `[DistributedLock("resource:{PropertyName}")]` trên Command/Query → behavior tự acquire/release
- DI: `builder.AddSharedCache("redis")`; AppHost: `.WithReference(redis).WaitFor(redis)`
- Doc: `docs/shared/shared-cache.md`

### Layered DI Rule (Inventory là mẫu)
- **Application** chỉ DI **MediatR + FluentValidation** → `AddApplication()` = `AddMediatorWithPielineDefault(AssemblyReference.Assembly)`. KHÔNG register repository/job/consumer/HTTP client ở Application.
- **Infrastructure** giữ toàn bộ implementation outbound: consumer, ConsumerDefinition, job, HTTP client, options + `IValidateOptions`. DI qua `AddInfrastructure()` ở `Infrastructure/DependencyInjection/Extensions/`.
- **Persistence** giữ `IUnitOfWork`, repository impl, `DbContext`, migrations → `AddPersistence()`.
- **API/Program.cs thứ tự**: `AddNpgsqlDbContext` → `AddInfrastructure()` → `AddApplication()` → `AddConfigMessaging` + `AddMessaging(bus.AddConsumer<...>(typeof(...Def)))` → `AddPersistence()` → `AddCarter` + versioning + Hangfire.
- Options đọc từ `appsettings` ĐẶT TẠI `Infrastructure/DependencyInjection/Options/` (kể cả options dùng cho consumer/job/HTTP client). Namespace: `UrbanX.<Service>.Infrastructure.DependencyInjection.Options`.
- Rule SOLID: Application biết **mình cần gì** (interface trong `Application/Clients` hoặc `Application/Abstractions`), Infrastructure biết **cách làm** (implementation).

### Integration Event Consumer (simple pattern)
- Consumer trực tiếp implement `IConsumer<TEvent>` (MassTransit) — KHÔNG còn `IntegrationEventConsumerBase`, KHÔNG Processor class trung gian, KHÔNG `CommandFailedException` indirection.
- Inject `ISender` (MediatR) + `ILogger<T>` → `Consume(ConsumeContext<T>)` build Command rồi `_sender.Send(cmd, ctx.CancellationToken)`.
- ConsumerDefinition đặt cùng namespace với Consumer (Infrastructure/Messaging) — đọc Options qua `IOptions<T>` để config queue name, retry, prefetch, concurrent limit, RabbitMQ topology binding.
- Đăng ký trong `Program.cs`: `bus.AddConsumer<XConsumer>(typeof(XConsumerDefinition))`.
- Ví dụ: [ReserveInventoryRequestedConsumer.cs](src/Services/Inventory/UrbanX.Inventory.Infrastructure/Messaging/ReserveInventoryRequested/ReserveInventoryRequestedConsumer.cs)

### Command Markers — `ICommand` vs `IMessagingCommand`
Phân biệt nguồn dispatch để pipeline behavior áp dụng đúng:

| Marker | Dispatched từ | Pipeline behaviors áp dụng |
|---|---|---|
| `ICommand` / `ICommand<T>` (kế thừa `ICommandBase`) | API endpoint (Carter) | Logging, Authorization, Validation, Idempotency (nếu có `IIdempotentCommand`), DistributedLock, **Transaction** |
| `IMessagingCommand` / `IMessagingCommand<T>` | MassTransit consumer | Logging, Authorization, Validation, DistributedLock — **SKIP Transaction, SKIP Idempotency** |

**Lý do**:
- MT EF Outbox đã wrap consumer trong `BeginTransactionAsync` → `SaveChangesAsync` → `CommitAsync` (`EntityFrameworkBusOutboxConsumeFilter<TDbContext>`). Wrap lại bằng `TransactionPipelineBehavior` ⇒ Npgsql "already in transaction" hoặc rollback semantics vỡ (handler trả `Result.Failure` → behavior swallow exception → MT vẫn commit `inbox_state` ⇒ silent "marked done nhưng chưa làm gì").
- MT InboxState (`inbox_state` table + `DuplicateDetectionWindow`) đã dedup theo MessageId → không cần custom `IIdempotentCommand` logic.
- `IMessagingCommand` KHÔNG kế thừa `ICommandBase` → `TransactionPipelineBehavior` (constrain `where TRequest : ICommandBase`) tự skip; `IdempotencyPipelineBehavior` (constrain `where TRequest : IIdempotentCommand`) cũng skip.

**Behavioral note cho IMessagingCommand handler:**
- KHÔNG gọi `SaveChanges` thủ công — MT auto-commit sau khi `Consume` return.
- KHÔNG có rollback tự động khi trả `Result.Failure`. Muốn rollback → `throw` (MT retry hoặc DLQ tuỳ ConsumerDefinition).
- Reference: [ReserveInventoryCommand.cs](src/Services/Inventory/UrbanX.Inventory.Application/Usecases/V1/Command/Reserve/ReserveInventoryCommand.cs) + [Handler](src/Services/Inventory/UrbanX.Inventory.Application/Usecases/V1/Command/Reserve/ReserveInventoryCommandHandler.cs).

### Transactional Outbox (MassTransit EF Outbox)
- DbContext register MT entities: `builder.AddInboxStateEntity(); builder.AddOutboxMessageEntity(); builder.AddOutboxStateEntity();` trong `OnModelCreating`
- Program.cs: `bus.AddEntityFrameworkOutbox<TDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); o.QueryDelay = TimeSpan.FromSeconds(1); o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10); });` trong `AddMessaging(configureBus: ...)`
- Handler inject `IEventPublisher` (từ `Shared.Application`) → `await _eventPublisher.PublishAsync(evt, ct);`. MT bus outbox intercept publish trong EF transaction, stage vào `outbox_message` rows cùng SaveChanges
- `BusOutboxDeliveryService` (MT auto-register) poll outbox và publish lên RabbitMQ; at-least-once guarantee + MessageId dedup trong `DuplicateDetectionWindow`

### Saga Choreography (Order flow — planned)
Order → Inventory → Payment → Merchant. Base classes: `SagaStateMachineBase`, `CompensatableActivityBase` trong `Shared.Messaging/Saga/`.

### Gateway Routes (YARP + Duende.BFF)
- `/api/v1/catalog/**` → Catalog · `/api/v1/inventory/**` → Inventory · `/api/v1/identity/**` → Identity
- `/api/orders/**` → Order · `/api/payments/**` → Payment
- BFF: `/bff/{login,logout,user}` · OIDC: `/connect/**`, `/.well-known/**`
- Rate limits: global 1000/60s · auth 10/60s · write 50/60s

---

## Thêm Feature Mới — Thứ tự

Domain → Persistence → Application → API → Gateway → Docs

1. Entity/value object (Domain)
2. Command hoặc Query + Validator + Handler (Application) — gắn `[RequirePermission]` hoặc `[AllowAnonymous]`
3. DbContext config, repo mới nếu cần (Persistence) → `dotnet ef migrations add <Name>`
4. Carter endpoint (API) — KHÔNG `RequireAuthorization()`
5. Route mới trong Gateway nếu cần
6. `docs/<service>/<feature>.md`

---

## NuGet — Packages chính

| Package | Version | Dùng cho |
|---|---|---|
| Carter | 10.0.0 | Minimal API routing |
| MediatR | 14.1.0 | CQRS dispatch |
| FluentValidation | 12.1.1 | Input validation |
| MassTransit.RabbitMQ | 8.1.3 | Messaging |
| EF Core / Npgsql | 10.0.x | Data access |
| Duende.IdentityServer | 7.4.6 | OAuth2/OIDC |
| Yarp.ReverseProxy | 2.3.0 | API Gateway |
| Stripe.net | 50.3.0 | Payments |
| OpenTelemetry | 1.15.2 | Tracing/metrics |
| Dapper | 2.1.35 | Micro-ORM cho read schema queries (Catalog) |
| Aspire.StackExchange.Redis | 13.1.3 | Redis client (IConnectionMultiplexer) |
| Microsoft.Extensions.Caching.StackExchangeRedis | 10.0.0 | IDistributedCache → Redis |

Thêm package mới: **chỉ sửa `Directory.Packages.props`**, không sửa `.csproj` trực tiếp.

---

## Unit Testing — Quick Reference

Framework: **xUnit 2.9.3** · Mock: **Moq 4.20.72** · Assertions: xUnit `Assert` (no FluentAssertions)

- Test file location: `tests/UrbanX.Services.Catalog.UnitTests/Usecases/V1/Command/<Feature>/`
- Naming: `{SubjectUnderTest}Tests` / `{Method}_{Scenario}_{ExpectedResult}`
- Always mock async repo methods with `It.IsAny<CancellationToken>()`
- Validator tests: use `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`)
- Full guide: `.claude/skills/unit-test-writer/SKILL.md`

**Test project `.csproj` dependencies (đã setup):**
```xml
<PackageReference Include="FluentValidation" />
<ProjectReference Include="..\..\src\Services\Catalog\UrbanX.Catalog.Application\UrbanX.Catalog.Application.csproj" />
```

---

## Rules
@.claude/rules/response-rules.md
@.claude/rules/achitecture.md
@.claude/rules/shared-rules.md
@.claude/rules/rtk-rules.md

## Skills & Agents
| Task | Dùng |
|---|---|
| Tạo Command             | skill `add-command` — đọc `.claude/skills/add-command/SKILL.md` |
| Tạo Query               | skill `add-query` — đọc `.claude/skills/add-query/SKILL.md` |
| Thêm Consumer           | skill `add-consumer` — đọc `.claude/skills/add-consumer/SKILL.md` |
| Review code C#          | skill `code-reviewer` hoặc agent `code-reviewer` |
| Viết unit test          | skill `unit-test-writer` — đọc `.claude/skills/unit-test-writer/SKILL.md` |
| Viết integration test   | skill `integration-test-writer` |
| Tạo EF migration        | skill `migration-generator` |
| Thêm service mới        | skill `add-service` |
| Lên plan feature        | agent `make-plan` |

Skill files: `.claude/skills/<name>/SKILL.md`
Agent files: `.claude/agents/<name>.md`
Rules: `.claude/rules/<name>.md`
