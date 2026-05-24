# UrbanX — Multi-Merchant Commerce Platform

UrbanX is a sample e-commerce platform built to demonstrate how a real-world application is structured using modern software engineering techniques. It is designed for educational purposes, with code organised to make each concept as clear as possible.

The system is built as a set of **microservices** — small, independent backend services that each handle one area of the business. A React frontend provides the customer-facing interface. Everything runs behind an API Gateway that acts as the single entry point for all requests.

---

## What This Project Teaches

- **Microservices architecture** — breaking a large application into small, independently deployable services
- **Event-driven communication** — services talk to each other by publishing and subscribing to integration events via **RabbitMQ + MassTransit**
- **CQRS (Command Query Responsibility Segregation)** — separating write operations (EF Core) from read operations (PostgreSQL read schema via Dapper)
- **Saga pattern (choreography-based)** — coordinating a multi-step business process (place order → reserve stock → process payment) without a central controller
- **Transactional outbox** — guarantees messages are never lost even if a service crashes after saving data but before sending the message (MassTransit EF Outbox)
- **Trust-the-Gateway auth** — the Gateway verifies the JWT once and forwards enriched identity headers; downstream services do not re-verify tokens
- **Policy-based authorization via MediatR pipeline** — `[RequirePermission]` / `[RequireRole]` attributes on Commands and Queries enforced by `AuthorizationPipelineBehavior`
- **Distributed caching & locking** — Redis-backed cache and `[DistributedLock]` attribute-driven locks via a MediatR pipeline behavior
- **API Gateway with rate limiting** — a single front door that protects backend services (YARP + Duende.BFF)
- **OpenTelemetry and distributed tracing** — observing what happens across many services when a single request is processed

---

## System Overview

When a customer places an order, the following sequence of events happens automatically across multiple services:

1. The customer browses products (Catalog Service) and applies a coupon/promotion (Promotion Service).
2. The Order Service creates a pending order slot, then dispatches a saga that publishes an `OrderCreated` event.
3. The Inventory Service reserves stock; on success it publishes `InventoryReserved`, on failure `InventoryReservationFailed`.
4. The Payment Service processes the charge (MoMo / SePay webhook) and publishes `PaymentCompleted` or `PaymentFailed`.
5. On payment failure, compensation events cancel the order and release the reserved inventory.

This flow uses the **Saga (choreography)** pattern — no single service controls the whole process; each service reacts to integration events from the previous step.

---

## Architecture

### Services

| Service | Port | Status | DB | Description |
|---------|------|--------|----|-------------|
| **API Gateway** | 5000 | Active | — | YARP reverse proxy + Duende.BFF cookie session. Enforces JWT auth, rate limiting, CORS, and forwards enriched identity headers. |
| **Identity** | 5005 | Active | `urbanx_identity` | Duende IdentityServer 7 + ASP.NET Core Identity. Issues JWTs, manages users/roles/permissions, Google OAuth, account lockout, audit log. |
| **Catalog** | 5025 | Active | `urbanx_catalog` | Product & category management. CQRS: writes via EF Core, reads via Dapper on a PostgreSQL read schema (`read.*`). MassTransit EF Outbox. |
| **Inventory** | 5020 | Active | `urbanx_inventory` | Stock tracking and reservation. Consumers for `ReserveInventoryRequested` and `ConfirmInventoryReservation`. Hangfire TTL job for expired reservations. ⭐ Reference layout for new services. |
| **Order** | 5010 | Active | `urbanx_order` | Shopping order creation and tracking. Saga choreography (PlaceOrderNormal / PlaceSalesOrder). Async ticket-polling flow. Product-variant read model (local projection). |
| **Payment** | 5015 | Active | `urbanx_payment` | Payment processing via MoMo and SePay webhook integrations. Auto-refund on saga compensation. |
| **Promotion** | 5040 | Active | `urbanx_promotion` | Coupons, voucher codes, flash sales. Coupon hold/release (distributed lock) + redemption used by Order saga. |

### Frontend

React 19 SPA located in `front-end/`. Connects to the backend exclusively through the API Gateway and authenticates users via OpenID Connect (OIDC). Managed by Aspire alongside backend services.

### Infrastructure (Aspire-managed)

| Component | Purpose |
|-----------|---------|
| **PostgreSQL 16** | Each service has its own isolated database (database-per-service). |
| **RabbitMQ** | Message broker for asynchronous event-driven communication via MassTransit. |
| **Redis** | Distributed cache (`ICacheService`) and distributed locking (`IDistributedLockService`). |
| **.NET Aspire** | Development orchestration — starts all services together, provides service discovery, health checks, and a live observability dashboard. |

---

## Architecture Patterns Explained

### Transactional Outbox (MassTransit EF Outbox)

**The problem:** A service saves data to its database and then needs to publish an event to RabbitMQ. If the service crashes between these two steps, data is saved but the event is never sent.

**How it works:** Instead of publishing directly, the handler publishes via `IEventPublisher`. MassTransit's bus outbox intercepts this publish within the EF Core transaction and stages it as a row in `outbox_message` alongside business data — in the same `SaveChanges`. A background `BusOutboxDeliveryService` then relays messages to RabbitMQ with at-least-once delivery and MessageId-based deduplication.

Services using this pattern: Catalog, Identity, Inventory, Order, Payment, Promotion.

### Saga (Choreography)

There is no central coordinator. Each service listens for integration events and reacts:

- Order Service publishes `OrderCreated`
- Inventory Service reserves stock → publishes `InventoryReserved` or `InventoryReservationFailed`
- Payment Service charges the customer → publishes `PaymentCompleted` or `PaymentFailed`
- On failure: compensation events cancel the order and release stock / coupons

### CQRS

**Catalog service example:** Writes go through EF Core (the command side). Integration events flow via Outbox → RabbitMQ → Catalog's own projection consumers, which rebuild `read.product_list_view` and `read.product_detail_view`. Read requests use Dapper to query these views directly.

### Trust-the-Gateway Auth

The API Gateway verifies the JWT once, strips the `Authorization` header, then forwards enriched headers to downstream services:

```
X-User-Id          → user identity
X-User-Roles       → role claims
X-Merchant-Id      → merchant scope
X-Permission-Scope → permission scope
```

Downstream services read identity via `IUserContext` (injected into handlers). `AuthorizationPipelineBehavior` enforces `[RequirePermission]` / `[RequireRole]` attributes declared on each Command or Query.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Docker and Docker Compose](https://www.docker.com/)

### Option 1: .NET Aspire (Recommended)

Aspire automatically starts PostgreSQL, RabbitMQ, and Redis in Docker, connects all services together, and opens a dashboard with logs and traces from every service.

**Step 1:** Install the Aspire workload (once only):

```bash
dotnet workload install aspire
```

**Step 2:** Start all backend services:

```bash
cd src/AppHost/UrbanX.AppHost
dotnet run
```

Aspire Dashboard opens at **`http://localhost:15260`**. Wait until all services show as healthy.

The frontend is started automatically by Aspire via a Vite app resource.

### Option 2: Infrastructure Only (Docker Compose)

Starts PostgreSQL and RabbitMQ for running services manually:

```bash
docker-compose up -d
```

Then start the Identity Service first (other services depend on it for token validation), followed by the remaining services:

```bash
# Terminal 1 — Identity (start first)
cd src/Services/Identity/UrbanX.Identity.API && dotnet run

# Terminal 2 — Catalog
cd src/Services/Catalog/UrbanX.Catalog.API && dotnet run

# Terminal 3 — Inventory
cd src/Services/Inventory/UrbanX.Inventory.API && dotnet run

# Terminal 4 — Order
cd src/Services/Order/UrbanX.Order.API && dotnet run

# Terminal 5 — Payment
cd src/Services/Payment/UrbanX.Payment.API && dotnet run

# Terminal 6 — Promotion
cd src/Services/Promotion/UrbanX.Promotion.API && dotnet run

# Terminal 7 — Gateway
cd src/Gateway/UrbanX.Gateway && dotnet run
```

---

## API Endpoints

All requests from the frontend go through the API Gateway. Endpoints marked **[Auth]** require a valid BFF cookie session (`urbanx.bff` HttpOnly). See `docs/gateway/bff.md` and `docs/auth/trust-gateway-flow.md`.

### Catalog — `/api/v1/catalog`

| Method | Path | Access | Description |
|--------|------|--------|-------------|
| GET | `/api/v1/catalog/products` | Public | Search and list products |
| GET | `/api/v1/catalog/product/{id}` | Public | Get product detail |
| POST | `/api/v1/catalog/product` | [Auth] Merchant | Create a product |
| PUT | `/api/v1/catalog/product/{id}/variants` | [Auth] Merchant | Update product variants |
| POST | `/api/v1/catalog/category` | [Auth] Admin | Create a category |
| PUT | `/api/v1/catalog/category/{id}` | [Auth] Admin | Update a category |
| DELETE | `/api/v1/catalog/category/{id}` | [Auth] Admin | Delete a category |

### Order — `/api/v1/orders`

| Method | Path | Access | Description |
|--------|------|--------|-------------|
| POST | `/api/v1/orders/` | [Auth] Customer | Place a standard order |
| POST | `/api/v1/orders/sales` | [Auth] Customer | Place a sales (flash-sale) order |
| GET | `/api/v1/orders/sales/{id}/status` | [Auth] Customer | Poll async sales order status |
| GET | `/api/v1/orders/ticket/{ticketId}` | [Auth] Customer | Get order by ticket |
| GET | `/api/v1/orders/my` | [Auth] Customer | List my orders |
| GET | `/api/v1/orders/{id}` | [Auth] Customer | Get order by ID |
| PUT | `/api/v1/orders/{id}/cancel` | [Auth] Customer | Cancel an order |

### Payment — `/api/v1/payments`

| Method | Path | Access | Description |
|--------|------|--------|-------------|
| GET | `/api/v1/payments/` | [Auth] | List payments |
| GET | `/api/v1/payments/{id}` | [Auth] | Get payment by ID |
| GET | `/api/v1/payments/by-order/{orderId}` | [Auth] | Get payment by order |
| POST | `/api/v1/payments/webhook/momo` | Public | MoMo IPN webhook |
| POST | `/api/v1/payments/webhook/sepay` | Public | SePay webhook |
| GET | `/api/v1/payments/{paymentId}/refunds/` | [Auth] | List refunds for a payment |
| GET | `/api/v1/payments/{paymentId}/refunds/{id}` | [Auth] | Get refund |

### Promotion — `/api/v1/promotions`

| Method | Path | Access | Description |
|--------|------|--------|-------------|
| POST | `/api/v1/promotions/` | [Auth] Admin | Create promotion |
| GET | `/api/v1/promotions/` | [Auth] | List promotions |
| GET | `/api/v1/promotions/{id}` | [Auth] | Get promotion |
| PUT | `/api/v1/promotions/{id}` | [Auth] Admin | Update promotion |
| POST | `/api/v1/promotions/{id}/activate` | [Auth] Admin | Activate promotion |
| POST | `/api/v1/promotions/{id}/pause` | [Auth] Admin | Pause promotion |
| POST | `/api/v1/promotions/{id}/voucher-codes` | [Auth] Admin | Add voucher codes |
| POST | `/api/v1/promotions/{id}/flash-sale-items` | [Auth] Admin | Add flash-sale items |
| POST | `/api/v1/promotions/preview` | [Auth] | Preview discount |

### Identity — `/api/account` and `/api/v1/identity`

| Method | Path | Access | Description |
|--------|------|--------|-------------|
| POST | `/api/account/register` | Public | Register a new user |
| POST | `/api/account/confirm-email` | Public | Confirm email address |
| POST | `/api/account/forgot-password` | Public | Request password reset |
| POST | `/api/account/reset-password` | Public | Reset password |
| POST | `/api/account/change-password` | [Auth] | Change password |
| GET | `/api/v1/identity/profile` | [Auth] | Get own profile |
| PUT | `/api/v1/identity/profile` | [Auth] | Update profile |
| GET | `/api/v1/identity/users` | [Auth] Admin | List users |
| GET | `/api/v1/identity/roles` | [Auth] Admin | List roles |
| POST | `/api/v1/identity/roles` | [Auth] Admin | Create role |
| GET | `/api/v1/identity/audit-logs` | [Auth] Admin | Get audit logs |

OIDC endpoints (`/connect/**`, `/.well-known/**`) are proxied directly to the Identity Service.

---

## Technology Stack

### Backend

| Technology | Purpose |
|------------|---------|
| .NET 10 / ASP.NET Core | Main framework for all backend services (Minimal API style) |
| .NET Aspire | Development orchestration, service discovery, health checks, observability dashboard |
| Carter | Minimal API module system for defining endpoints |
| MediatR | In-process CQRS dispatch (commands, queries, pipeline behaviors) |
| FluentValidation | Input validation integrated into the MediatR pipeline |
| MassTransit + RabbitMQ | Asynchronous integration events and transactional outbox |
| Entity Framework Core 10 / Npgsql | ORM for write-side data access |
| Dapper | Micro-ORM for read-schema queries (Catalog read model) |
| PostgreSQL 16 | Relational database — each service has its own isolated database |
| Redis | Distributed cache and distributed locking |
| Hangfire | Recurring background jobs (e.g., Inventory reservation TTL expiry) |
| Duende IdentityServer 7 | OpenID Connect and OAuth 2.0 server for issuing JWTs |
| YARP | Reverse proxy used to build the API Gateway |
| Duende.BFF | Cookie-based BFF session for the React SPA |
| OpenTelemetry | Distributed tracing and metrics collection |

### Frontend

| Technology | Purpose |
|------------|---------|
| React 19 + TypeScript | UI library |
| Vite | Development server and build tool |
| Tailwind CSS 4 | Utility-first CSS framework |
| React Router | Client-side routing |
| oidc-client-ts | Handles OpenID Connect login flow |

### Shared Libraries (`src/Shared/`)

| Library | Purpose |
|---------|---------|
| `Shared.Kernel` | Domain primitives: `Error`, `Result<T>`, `DomainException`, `BaseEntity<TKey>`, `IUnitOfWork`, `GatewayHeaderNames` |
| `Shared.Contract` | Cross-service contracts: `IIntegrationEvent`, `IntegrationEventBase`, integration event DTOs |
| `Shared.Application` | CQRS abstractions: `ICommand`, `IQuery`, `IMessagingCommand`, `IEventPublisher`, `ISagaState`, `IUserContext`, `Permissions`, authorization attributes |
| `Shared.Cache` | Redis cache (`ICacheService`), distributed lock (`IDistributedLockService`), `[DistributedLock]` attribute |
| `Shared.Observability` | OpenTelemetry configuration: tracing, metrics, OTLP exporter |

`Shared.Messaging` provides: MassTransit + RabbitMQ configuration, MediatR pipeline behaviors (Authorization → Idempotency → Validation → DistributedLock → Transaction), saga base classes, `EventPublisher` implementation, and Trust-Gateway user context middleware.

---

## Project Structure

```
urbanx-sample/
├── src/
│   ├── AppHost/UrbanX.AppHost/           # Aspire host — starts and wires all services
│   ├── ServiceDefaults/                  # Shared Aspire defaults (health, telemetry, resilience)
│   ├── Services/
│   │   ├── Catalog/                      # Product catalog (CQRS + MT EF Outbox + Dapper read schema)
│   │   │   ├── UrbanX.Catalog.API/
│   │   │   ├── UrbanX.Catalog.Application/
│   │   │   ├── UrbanX.Catalog.Domain/
│   │   │   ├── UrbanX.Catalog.Infrastructure/
│   │   │   └── UrbanX.Catalog.Persistence/
│   │   ├── Inventory/                    # ⭐ Reference layout for new services
│   │   │   ├── UrbanX.Inventory.API/
│   │   │   ├── UrbanX.Inventory.Application/
│   │   │   ├── UrbanX.Inventory.Domain/
│   │   │   ├── UrbanX.Inventory.Infrastructure/   # Consumer + ConsumerDefinition + Hangfire job
│   │   │   └── UrbanX.Inventory.Persistence/
│   │   ├── Order/                        # Order saga choreography
│   │   ├── Payment/                      # MoMo / SePay webhook + auto-refund
│   │   ├── Promotion/                    # Coupons, voucher codes, flash sales
│   │   └── Identity/                     # Duende IdentityServer + ASP.NET Identity
│   ├── Gateway/
│   │   ├── UrbanX.Gateway/              # YARP reverse proxy, BFF, rate limiting
│   │   ├── UrbanX.Gateway.Application/
│   │   └── UrbanX.Gateway.Infrastructure/ # RBAC, header enrichment, observability
│   ├── Shared/
│   │   ├── Shared.Kernel/               # Domain primitives (Error, Result, BaseEntity, …)
│   │   ├── Shared.Contract/             # Cross-service integration event contracts
│   │   ├── Shared.Application/          # CQRS abstractions + authorization contracts
│   │   ├── Shared.Cache/                # Redis cache + distributed lock
│   │   └── Shared.Observability/        # OpenTelemetry wiring
│   └── front-end/                       # React 19 SPA (Vite)
├── tests/
│   ├── UrbanX.Services.Catalog.UnitTests/
│   ├── UrbanX.Services.Catalog.IntegrationTests/
│   ├── UrbanX.Services.Inventory.UnitTests/
│   ├── UrbanX.Services.Order.UnitTests/
│   ├── UrbanX.Services.Promotion.UnitTests/
│   └── UrbanX.Shared.Messaging.UnitTests/
├── docs/                                 # Per-service documentation
├── docker/                               # Dockerfiles
├── kubernetes/                           # Kubernetes manifests
├── docker-compose.yml                    # Local infrastructure (PostgreSQL, RabbitMQ)
├── docker-compose.production.yml         # Production Docker Compose
├── Directory.Packages.props              # Centralised NuGet version management
└── UrbanX.sln
```

---

## Development Guide

### Running Tests

```bash
# All tests
dotnet test UrbanX.sln

# Catalog unit tests only
dotnet test tests/UrbanX.Services.Catalog.UnitTests/UrbanX.Services.Catalog.UnitTests.csproj
```

Tests use **xUnit 2.9.3** + **Moq 4.20.72**. Assertions use xUnit's built-in `Assert` (no FluentAssertions).

### EF Core Migrations

```bash
# From the service's Persistence project directory
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

### Adding a New NuGet Package

Add the version entry to **`Directory.Packages.props`** only. Do not add `<Version>` to individual `.csproj` files.

### Hot Reload

```bash
# Frontend — automatic via Vite
npm run dev

# Backend
dotnet watch run
```

### Environment Configuration

```bash
cp .env.example .env
# Fill in required values (DB passwords, MoMo/SePay keys, etc.)
```

Sensitive values (API keys, secrets) belong in `appsettings.Development.json` or environment variables — never committed to source control.

### Troubleshooting

**Port already in use:** Change the port in `Properties/launchSettings.json`.

**RabbitMQ not connecting:**
```bash
docker-compose ps
docker-compose up -d rabbitmq
```

**Database connection problems:**
```bash
docker-compose up -d postgres
```

**Frontend not loading:**
```bash
cd front-end
rm -rf node_modules
npm install
```

**Build errors:**
```bash
dotnet clean && dotnet build
```

---

## Security

- **Trust-the-Gateway auth** — the API Gateway is the single JWT verification point. Downstream services read identity from enriched headers via `IUserContext`.
- **Policy-based authorization** — `[RequirePermission]` / `[RequireRole]` attributes on Commands/Queries are enforced by `AuthorizationPipelineBehavior` in the MediatR pipeline. Permission constants are centralised in `Shared.Application/Authorization/Permissions.cs`.
- **Rate limiting** — Global 1 000 req/60 s per IP; auth endpoints 10/60 s; write operations 50/60 s; sales-order burst bucket (token bucket).
- **Distributed lock** — `[DistributedLock]` on Commands/Queries prevents concurrent execution of the same logical operation (e.g., coupon hold).
- **Input validation** — FluentValidation validators run in the MediatR pipeline before every handler.
- **Transactional outbox** — at-least-once event delivery with MessageId-based deduplication prevents double-processing.
- **Account lockout** — 5 failed login attempts → 15-minute lockout (Identity Service).

---

## Deployment

### Docker Compose (Production)

```bash
docker-compose -f docker-compose.production.yml up -d
```

### Kubernetes

```bash
kubectl apply -f kubernetes/
```

---

## License

MIT
