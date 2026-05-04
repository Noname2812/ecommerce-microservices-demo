---
description: 
alwaysApply: true
---

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

UrbanX is a learning project demonstrating microservices patterns on .NET 10. Catalog, Search, API Gateway, Inventory, and Identity are currently scaffolded and active. Order, Merchant, and Payment are planned but their AppHost registrations are commented out.

**Important discrepancy:** The README describes Apache Kafka as the message broker, but the actual implementation uses **RabbitMQ via MassTransit**.

## Commands

### Run the full stack (recommended)
```bash
dotnet workload install aspire  # one-time setup
cd src/AppHost/UrbanX.AppHost
dotnet run
```
Aspire Dashboard: `http://localhost:15260`. Starts PostgreSQL, RabbitMQ, Elasticsearch, and Redis automatically.

### Run infrastructure only
```bash
docker-compose up -d
```

### Run a single service
```bash
cd src/Services/Catalog/UrbanX.Catalog.API && dotnet run
```
Identity Service must start first when running manually — other services depend on it for JWT validation.

### Frontend
```bash
cd src/Frontend/urbanx-react
npm install && npm run dev   # http://localhost:5173
```

### Build & test
```bash
dotnet build UrbanX.sln
dotnet test UrbanX.sln
dotnet test tests/UrbanX.Services.Catalog.UnitTests/UrbanX.Services.Catalog.UnitTests.csproj
```

### EF Core migrations
```bash
# From the service's Persistence project directory
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## Architecture

### Service structure (Clean Architecture per service)
Each service is split into layers:
- `*.API` — Minimal API endpoints using **Carter** (`ICarterModule`), Program.cs wiring
- `*.Application` — CQRS use cases via **MediatR**: `Usecases/V1/Command/` and `Usecases/V1/Query/`
- `*.Domain` — Entities, domain events, value objects
- `*.Infrastructure` — EF Core DbContext, repositories, external service clients
- `*.Persistence` — EF Core migrations (separate project to keep migrations isolated)

### Shared libraries (`src/Shared/`)
| Library | Purpose |
|---|---|
| `Shared.Kernel` | Domain primitives: `Error`, `Result<T>`, `DomainException`, `BaseEntity<TKey>`, `IDateTracking`, `ISoftDelete`, `IUserTracking`, `IValidationResult`, `PageResult<T>`; also `GatewayHeaderNames` (shared header name constants) |
| `Shared.Contract` | Cross-service contracts only: `IIntegrationEvent`, `IntegrationEventBase`, integration event DTOs and events (Catalog, Identity) |
| `Shared.Application` | CQRS abstractions: `ICommand`, `IQuery`, handler interfaces, `IDomainEvent`, `IEventPublisher`, `ISagaState`; authorization (`IUserContext`, `Permissions`/`Roles` constants, `[RequirePermission]`/`[RequireRole]`/`[AllowAnonymous]` attributes, `PermissionScope`) |
| `Shared.Messaging` | MassTransit + RabbitMQ config, MediatR pipeline behaviors (`Validation`, `Logging`, `Idempotency`, `DistributedLock`, `Authorization`, `Transaction`), saga infrastructure, `EventPublisher` impl, `UserHttpContext` + `UserContextMiddleware` (Trust-Gateway) |
| `Shared.Cache` | Redis cache: `ICacheService` (get/set/getOrSet/Lua), `IDistributedLockService` (SET NX, cluster-safe), `[DistributedLock]` attribute; `IDistributedCache` backend (fixes `IdempotencyPipelineBehavior`); DI entry point: `builder.AddSharedCache("redis")` |
| `Shared.Outbox` | Transactional outbox: `OutboxMessage`, `OutboxRelayWorker`, `IOutboxWriter` |
| `Shared.Observability` | OpenTelemetry configuration: metrics, activity sources, OTLP exporter |

**Dependency order (lower = fewer deps):** Shared.Kernel → Shared.Cache → Shared.Contract → Shared.Application → Shared.Messaging → services

### Key patterns

**Transactional Outbox:** Commands save business data + an outbox event in one EF Core transaction. `OutboxRelayService` reads the outbox table and publishes to RabbitMQ, guaranteeing at-least-once delivery. Catalog and Payment use this.

**CQRS (Catalog + Search):** Writes go to PostgreSQL (Catalog service). Catalog publishes events that Search service consumes to update Elasticsearch. Read queries from clients route to the Search API via the Gateway.

**Saga choreography:** The planned order flow (Order → Inventory → Payment → Merchant) uses choreography — each service reacts to integration events from the previous step and emits its own, with compensation events on failure.

**MediatR pipeline:** `TransactionPipelineBehavior` wraps command handlers in a DB transaction via `IUnitOfWork` (implemented by `EfUnitOfWork` in each service's Persistence layer). Behaviors registered by default via `AddMediatorWithPielineDefault`: Authorization → Idempotency → Validation → DistributedLock → Transaction. `AuthorizationPipelineBehavior` reflects `[RequirePermission]`/`[RequireRole]`/`[AllowAnonymous]` attributes on Command/Query and validates against `IUserContext`. `DistributedLockPipelineBehavior` acquires a Redis lock when `[DistributedLock]` is present.

**Distributed Cache (Shared.Cache):** Redis-backed cache and distributed lock. `ICacheService` provides get/set/getOrSet/pattern-delete/Lua eval. `IDistributedLockService` provides `TryAcquireAsync` (non-blocking) and `AcquireAsync` (poll with timeout), implemented with `SET NX PX` (cluster-safe). Enable per service: `builder.AddSharedCache("redis")` in `Program.cs`. See `docs/shared/shared-cache.md`.

**Trust-the-Gateway auth:** Gateway verifies JWT once, enriches `X-User-Id`/`X-User-Roles`/`X-Merchant-Id`/`X-Permission-Scope` headers, strips Authorization before forwarding. Services do NOT verify JWT — they read identity from headers via `IUserContext` and authorize via MediatR behavior + ownership checks in handlers. See `docs/auth/trust-gateway-flow.md`.

**Identity service (OIDC issuer):** Duende IdentityServer 7.4.6 + ASP.NET Core Identity. Issues JWTs at `/connect/token`, exposes OIDC discovery at `/.well-known/openid-configuration`. Manages users/roles/profile/audit log. Handles email confirm + password reset (logged via `LogEmailSender` in dev), Google OAuth (when `Google:ClientId/Secret` set), account lockout (5 attempts → 15 min). Permission claims stored on roles (claim type `permission`); Gateway RBAC reads them to set `X-Permission-Scope`. See `docs/identity/`.

### API Gateway (`src/Gateway/UrbanX.Gateway`)
YARP reverse proxy. Routes all client requests to services, enforces JWT authentication, rate limiting, and CORS. Service URLs resolved via Aspire service discovery.

## Configuration

- `Directory.Packages.props` — centralized NuGet version management; add new packages here, not in individual `.csproj` files
- `.env.example` — copy to `.env` for manual (non-Aspire) setup; contains DB connection strings, Stripe keys, etc.
- Aspire handles connection strings and service URLs automatically in development via `WithReference()`

## Token Optimization
@.claude/rules/rtk-rules.md
