# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

UrbanX is a learning project demonstrating microservices patterns on .NET 10. Catalog, Search, API Gateway, and Inventory are currently scaffolded and active. Order, Merchant, Payment, and Identity are planned but their AppHost registrations are commented out.

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
| `Shared.Contract` | Cross-service contracts only: `IIntegrationEvent`, `IntegrationEventBase`, integration event DTOs and events (Catalog) |
| `Shared.Application` | CQRS abstractions: `ICommand`, `IQuery`, handler interfaces, `IDomainEvent`, `IEventPublisher`, `ISagaState` |
| `Shared.Messaging` | MassTransit + RabbitMQ config, MediatR pipeline behaviors (`Validation`, `Logging`, `Idempotency`, `Transaction`), saga infrastructure, `EventPublisher` impl |
| `Shared.Outbox` | Transactional outbox: `OutboxMessage`, `OutboxRelayWorker`, `IOutboxWriter` |
| `Shared.Observability` | OpenTelemetry configuration: metrics, activity sources, OTLP exporter |

**Dependency order (lower = fewer deps):** Shared.Kernel → Shared.Contract → Shared.Application → Shared.Messaging → services

### Key patterns

**Transactional Outbox:** Commands save business data + an outbox event in one EF Core transaction. `OutboxRelayService` reads the outbox table and publishes to RabbitMQ, guaranteeing at-least-once delivery. Catalog and Payment use this.

**CQRS (Catalog + Search):** Writes go to PostgreSQL (Catalog service). Catalog publishes events that Search service consumes to update Elasticsearch. Read queries from clients route to the Search API via the Gateway.

**Saga choreography:** The planned order flow (Order → Inventory → Payment → Merchant) uses choreography — each service reacts to integration events from the previous step and emits its own, with compensation events on failure.

**MediatR pipeline:** `CatalogTransactionBehavior` wraps command handlers in a DB transaction. Validation behaviors run FluentValidation before handlers.

### API Gateway (`src/Gateway/UrbanX.Gateway`)
YARP reverse proxy. Routes all client requests to services, enforces JWT authentication, rate limiting, and CORS. Service URLs resolved via Aspire service discovery.

## Configuration

- `Directory.Packages.props` — centralized NuGet version management; add new packages here, not in individual `.csproj` files
- `.env.example` — copy to `.env` for manual (non-Aspire) setup; contains DB connection strings, Stripe keys, etc.
- Aspire handles connection strings and service URLs automatically in development via `WithReference()`
