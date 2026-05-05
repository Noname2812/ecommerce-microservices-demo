# Project: UrbanX

.NET 10 microservices e-commerce — Clean Architecture, Carter, MediatR (CQRS), MassTransit + RabbitMQ, EF Core, Transactional Outbox, .NET Aspire.

---

## Service Map

| Service | Port | Status | DB | Notes |
|---|---|---|---|---|
| **Catalog** | 5025 | Active | PostgreSQL (`urbanx_catalog`) | CQRS + Outbox |
| **Search** | 5035 | Active | Elasticsearch | Consumes Catalog events |
| **Gateway** | 5000 | Active | — | YARP + Duende.BFF (cookie session) + Rate limiting |
| **Identity** | 5005 | Active | PostgreSQL (`urbanx_identity`) | Duende IdentityServer + ASP.NET Identity + Outbox |
| **Order** | 5010 | Disabled | PostgreSQL | Saga choreography |
| **Payment** | 5015 | Disabled | PostgreSQL | Stripe + Outbox |
| **Merchant** | 5030 | Disabled | PostgreSQL | — |
| **Inventory** | 5020 | Active | PostgreSQL (`urbanx_inventory`) | CQRS + Outbox; 4 entities: Warehouse, InventoryItem, Reservation, StockMovement |
| **Frontend** | 5173 | Disabled | — | React 19 + Vite |

Infrastructure (Aspire tự quản lý): PostgreSQL, RabbitMQ, Elasticsearch, Redis.

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
│   ├── Search/
│   │   ├── UrbanX.Search.API/
│   │   ├── UrbanX.Search.Application/
│   │   ├── UrbanX.Search.Infrastructure/
│   │   └── UrbanX.Search.Infrastructure.Elasticsearch/
│   ├── Inventory/
│   │   ├── UrbanX.Inventory.API/             # Carter modules (InventoryItemApis.cs)
│   │   ├── UrbanX.Inventory.Application/     # CQRS: Usecases/V1/Command|Query/
│   │   ├── UrbanX.Inventory.Domain/          # Models, ValueObjects, Repositories (interfaces)
│   │   ├── UrbanX.Inventory.Infrastructure/  # (trống, placeholder)
│   │   └── UrbanX.Inventory.Persistence/     # DbContext, Repos, Migrations
│   └── Identity/
│       ├── UrbanX.Identity.API/              # Carter (AccountApis, ProfileApis, UsersApis, RolesApis, AuditApis) + Duende IdentityServer + Google OAuth
│       ├── UrbanX.Identity.Application/      # CQRS: Register/ConfirmEmail/ForgotPassword/ResetPassword/ChangePassword/UpdateProfile/AssignRole/RevokeRole/Deactivate/Activate
│       ├── UrbanX.Identity.Domain/           # ApplicationUser/Role, UserProfile, AuthAuditLog, AuthEventType
│       ├── UrbanX.Identity.Infrastructure/   # IEmailSender (LogEmailSender), IIdentityAuditWriter
│       └── UrbanX.Identity.Persistence/      # IdentityDbContext (extends OutboxDbContext), Configurations, AuthAuditLogRepository
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
    ├── Shared.Outbox/          # OutboxMessage, OutboxRelayWorker, IOutboxWriter
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

### Transactional Outbox
- Inject `IOutboxWriter` trong command handler → ghi event cùng transaction với data
- `OutboxRelayWorker` publish lên RabbitMQ; dùng khi cần at-least-once guarantee

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
| Elastic.Clients.Elasticsearch | 9.3.0 | Search |
| Duende.IdentityServer | 7.4.6 | OAuth2/OIDC |
| Yarp.ReverseProxy | 2.3.0 | API Gateway |
| Stripe.net | 50.3.0 | Payments |
| OpenTelemetry | 1.15.2 | Tracing/metrics |
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
