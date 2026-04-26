# Project: UrbanX

.NET 10 microservices e-commerce — Clean Architecture, Carter, MediatR (CQRS), MassTransit + RabbitMQ, EF Core, Transactional Outbox, .NET Aspire.

---

## Service Map

| Service | Port | Status | DB | Notes |
|---|---|---|---|---|
| **Catalog** | 5290 | Active | PostgreSQL (`urbanx_catalog`) | CQRS + Outbox |
| **Search** | dynamic | Active | Elasticsearch | Consumes Catalog events |
| **Gateway** | dynamic | Active | — | YARP + JWT + Rate limiting |
| **Identity** | 5005 | Active | PostgreSQL (`urbanx_identity`) | Duende IdentityServer + ASP.NET Identity + Outbox |
| **Order** | 5002 | Disabled | PostgreSQL | Saga choreography |
| **Payment** | 5004 | Disabled | PostgreSQL | Stripe + Outbox |
| **Merchant** | 5003 | Disabled | PostgreSQL | — |
| **Inventory** | dynamic | Active | PostgreSQL (`urbanx_inventory`) | CQRS + Outbox; 4 entities: Warehouse, InventoryItem, Reservation, StockMovement |
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
    ├── Shared.Messaging/       # MassTransit + RabbitMQ config, MediatR pipeline behaviors, saga base, EventPublisher impl
    ├── Shared.Outbox/          # OutboxMessage, OutboxRelayWorker, IOutboxWriter
    └── Shared.Observability/   # OpenTelemetry wiring

docs/                           # Docs tổng quan (migrations, security, deployment)
tests/
├── UrbanX.Services.Catalog.UnitTests/      # xUnit + Moq, no DB
└── UrbanX.Services.Catalog.IntegrationTests/ # WebApplicationFactory + EF InMemory
```

---

## Key Patterns — Quick Reference

### CQRS (Catalog + Search)
- Command → `Usecases/V1/Command/<Name>/<Name>Command.cs` + `<Name>CommandHandler.cs`
- Query → `Usecases/V1/Query/<Name>/<Name>Query.cs` + `<Name>QueryHandler.cs`
- Mỗi Command/Query đi kèm Validator (`FluentValidation`)
- Gắn `[RequirePermission(Permissions.<Resource>.<Action>, MinScope = PermissionScope.Own|All)]` trên Command/Query class (hoặc `[AllowAnonymous]` cho public). **Bắt buộc dùng constants**, không string literal.
- Inject `IUserContext` vào handler khi cần `UserId` / ownership check
- `CatalogTransactionBehavior` tự bọc command trong DB transaction
- Sử dụng skill: `command` hoặc `query`

### Trust-the-Gateway Auth
- Gateway verify JWT, enrich `X-User-Id` / `X-User-Roles` / `X-Merchant-Id` / `X-Permission-Scope` headers, strip Authorization
- Service KHÔNG verify JWT, KHÔNG dùng `RequireAuthorization()` ở endpoints (Identity là exception — nó là JWT issuer)
- `app.UseUserContext()` + `IUserContext` (scoped) đọc identity từ headers
- `AuthorizationPipelineBehavior` reflect attribute → check IUserContext
- Permissions/Roles constants: `Shared.Application/Authorization/Permissions.cs` — `Products`, `Inventory`, `Users`, `Roles`
- Doc: `docs/auth/trust-gateway-flow.md`

### Identity Service (Duende IdentityServer)
- Issuer JWT cho toàn hệ thống tại port 5005
- Endpoints public: `/connect/{token,authorize,endsession,userinfo,revocation}`, `/.well-known/*`, `/api/account/{register,confirm-email,forgot-password,reset-password}`, `/signin-google`
- Endpoints authenticated: `/api/v1/identity/{me,profile,users,roles,audit-logs,...}`
- 2 clients (in-memory, dev): `urbanx-spa` (Auth Code + PKCE + offline_access), `urbanx-test-password` (resource owner password, dev secret)
- Permission claims trên Role: claim type `permission`, dùng cho Gateway RBAC + `IUserContext`
- `LogEmailSender` (dev) ghi log thay gửi email; thay bằng SMTP/SendGrid khi production
- Outbox publish: `UserRegistered`, `UserProfileUpdated`, `UserRoleAssigned`, `UserRoleRevoked`, `UserDeactivated`, `UserActivated` (`Shared.Contract/Messaging/Identity/`)
- Account lockout: 5 fails → 15 phút (config `Identity:Lockout`)
- Audit log: bảng `auth_audit_logs`, IP + UA capture qua `IIdentityAuditWriter`
- Doc: `docs/identity/`

### Transactional Outbox (Catalog, Payment)
- Command handler ghi data + `OutboxMessage` trong 1 transaction
- `OutboxRelayWorker` (background) đọc outbox → publish lên RabbitMQ
- `IOutboxWriter` inject từ `Shared.Outbox`

### Integration Events (cross-service)
Contracts ở `Shared.Contract/Messaging/`:
- `Catalog/ProductCreated.cs`
- `Catalog/ProductUpdateEvents.cs`
- Consumer kế thừa `IntegrationEventConsumerBase` từ `Shared.Messaging`
- Consumer đặt trong `*.Application/Messaging/`, đăng ký qua `bus.AddConsumer<>()` trong `Program.cs`
- Sử dụng skill: `add-consumer`

### Saga Choreography (Order flow — planned)
Order → Inventory → Payment → Merchant, mỗi service emit event kế tiếp.
Base classes: `SagaStateMachineBase`, `CompensatableActivityBase` trong `Shared.Messaging/Saga/`.

### Gateway (YARP)
Routes trong `appsettings.json`:
- `/api/v1/catalog/**` → Catalog (5290)
- `/api/orders/**` → Order (5002)
- `/api/payments/**` → Payment (5004)
- `/api/account/**`, `/connect/**` → Identity (5005)

Rate limits: global 1000 req/60s · auth 10 req/60s · search 60 req/60s · write 50 req/60s.

---

## Thêm Feature Mới — Checklist

Thứ tự chuẩn (Domain → Application → Persistence → API):

1. **Domain**: thêm entity/value object/domain event nếu cần
2. **Application**: tạo Command hoặc Query + Validator + Handler
   - Gắn `[RequirePermission(Permissions.<Resource>.<Action>)]` (hoặc `[AllowAnonymous]`)
   - Nếu cần `UserId` / ownership check → inject `IUserContext` vào handler
3. **Persistence**: cập nhật DbContext config, thêm repo nếu cần
4. **Migration**: `dotnet ef migrations add <Name>` từ `*.Persistence/`
5. **API**: thêm Carter module endpoint (KHÔNG `RequireAuthorization()` — authorization qua attribute trên Command/Query)
6. **Gateway**: cập nhật route nếu path mới
7. **Docs**: tạo `docs/<service>/<feature>.md`

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
Rules: `.claude/rules/response-rules.md`
