# Payment Service

.NET 10 — Clean Architecture, Carter, MediatR (CQRS), EF Core + PostgreSQL, Transactional Outbox, MassTransit.

Port: **5004** | DB: `urbanx_payment` | Connection string: `paymentdb` | Status: **Active**

---

## Projects

| Project | Responsibility |
|---|---|
| `UrbanX.Payment.Domain` | Entities, value objects, repository interfaces |
| `UrbanX.Payment.Application` | Commands, handlers, validators, consumers, error codes, MediatR behavior |
| `UrbanX.Payment.Persistence` | EF Core DbContext, entity configs, repos, migrations |
| `UrbanX.Payment.API` | Carter modules, HTTP endpoints, Trust-Gateway middleware, Program.cs |

---

## Domain

### Entities (inherit `BaseEntity<Guid>`)

**Payment** — aggregate chính
- Denormalized từ Order service (không có FK cross-service): `OrderId`, `OrderNumber`, `CustomerId`, `CustomerEmail`
- `ProviderId` → FK nội bộ → `PaymentProvider`
- `Amount (decimal 18,2)`, `Currency` (default "VND")
- `ProviderTransactionId?`, `ProviderResponse?` (jsonb), `PaymentMethodDetails?` (jsonb)
- `Status`: PENDING → PROCESSING → COMPLETED / FAILED / CANCELLED
- `IdempotencyKey` (unique) — ngăn double charge
- `IpAddress?`, `PaidAt?`, `CreatedAt`, `UpdatedAt`
- Domain methods: `MarkProcessing()`, `MarkCompleted(transactionId?)`, `MarkFailed(reason)`, `MarkCancelled()`

**PaymentProvider** — nguồn cấu hình thanh toán
- `Name`, `Type` (CARD / EWALLET / BANK_TRANSFER / COD), `Config?` (jsonb), `IsActive`, `SupportedCurrencies` (text[])

**Refund** — hoàn tiền
- `PaymentId` → FK → Payment (Restrict)
- `Amount`, `Reason?`, `ProviderRefundId?`, `Status` (PENDING / COMPLETED / FAILED), `ProcessedAt?`
- Domain methods: `MarkCompleted(providerRefundId?)`, `MarkFailed()`

**PaymentEvent** — audit log (append-only)
- `PaymentId` → FK → Payment (Cascade), `EventType`, `Payload?` (jsonb), `Source` (INTERNAL / WEBHOOK_*)

### Value Objects (static constants)

| Class | Values |
|---|---|
| `PaymentStatus` | PENDING, PROCESSING, COMPLETED, FAILED, CANCELLED |
| `RefundStatus` | PENDING, COMPLETED, FAILED |
| `ProviderType` | CARD, EWALLET, BANK_TRANSFER, COD |
| `EventSource` | INTERNAL, WEBHOOK_STRIPE, WEBHOOK_MOMO, WEBHOOK_VNPAY |

---

## Application

### Commands

| Command | Permission | Outbox Event |
|---|---|---|
| `CreatePaymentCommand` | `[AllowAnonymous]` | — |
| `ProcessPaymentCommand` | `payment:write` | — |
| `CompletePaymentCommand` | `payment:write` | `PaymentCompletedV1` |
| `FailPaymentCommand` | `payment:write` | `PaymentFailedV1` |
| `CancelPaymentCommand` | `payment:write` | — |
| `CreateRefundCommand` | `[AllowAnonymous]` | — |
| `CompleteRefundCommand` | `payment:write` | `RefundProcessedV1` |

**`CreatePaymentCommand`**: idempotent (check theo `IdempotencyKey`); Phase 1 dùng COD provider mặc định.

### Queries

| Query | Permission | Response |
|---|---|---|
| `GetPaymentByIdQuery` | `payment:read` | `PaymentDetailDto` |
| `GetPaymentByOrderIdQuery` | `payment:read` | `PaymentDetailDto` |
| `ListPaymentsQuery` | `payment:read` | `PageResult<PaymentDetailDto>` |
| `GetRefundByIdQuery` | `payment:read` | `RefundDto` |
| `ListRefundsByPaymentQuery` | `payment:read` | `IReadOnlyList<RefundDto>` |

### Consumers (MassTransit)

- **`OrderCreatedConsumer`** → `OrderIntegrationEvents.OrderCreatedV1` → dispatch `CreatePaymentCommand`
- **`OrderCancelledConsumer`** → `OrderIntegrationEvents.OrderCancelledV1` → dispatch `CancelPaymentCommand` (PENDING/PROCESSING) hoặc `CreateRefundCommand` (COMPLETED)

---

## API Endpoints

### Payments `/api/v1/payments`

| Method | Path | Command/Query |
|---|---|---|
| GET | `/` | `ListPaymentsQuery` |
| GET | `/{id}` | `GetPaymentByIdQuery` |
| GET | `/by-order/{orderId}` | `GetPaymentByOrderIdQuery` |
| POST | `/{id}/process` | `ProcessPaymentCommand` |
| POST | `/{id}/complete` | `CompletePaymentCommand` |
| POST | `/{id}/fail` | `FailPaymentCommand` |
| POST | `/{id}/cancel` | `CancelPaymentCommand` |

### Refunds `/api/v1/payments/{paymentId}/refunds`

| Method | Path | Command/Query |
|---|---|---|
| GET | `/` | `ListRefundsByPaymentQuery` |
| GET | `/{id}` | `GetRefundByIdQuery` |
| POST | `/{id}/complete` | `CompleteRefundCommand` |

---

## Integration Events

### Published (via Outbox)

| Event | Trigger |
|---|---|
| `PaymentCompletedV1` | `CompletePaymentCommandHandler` |
| `PaymentFailedV1` | `FailPaymentCommandHandler` |
| `RefundProcessedV1` | `CompleteRefundCommandHandler` |

### Consumed

| Event | Handler |
|---|---|
| `OrderCreatedV1` | `OrderCreatedConsumer` |
| `OrderCancelledV1` | `OrderCancelledConsumer` |

Contracts trong `Shared.Contract/Messaging/Payment/PaymentIntegrationEvents.cs`.

---

## AppHost Registration

```csharp
var paymentDb = postgres.AddDatabase("paymentdb", "urbanx_payment");

var paymentService = builder.AddProject<Projects.UrbanX_Payment_API>("payment")
    .WithReference(paymentDb)
    .WithReference(identityService)
    .WithReference(rabbitMq)
    .WaitFor(paymentDb)
    .WaitFor(identityService)
    .WaitFor(rabbitMq);

gateway.WithReference(paymentService).WaitFor(paymentService);
```

## Gateway Route

`payment-route` → `/api/v1/payments/{**catch-all}` → `payment-cluster` (port 5004)

---

## Scope — Phase 1

Phase 1 không tích hợp payment gateway thực tế (Stripe/MoMo). Tất cả payment dùng COD provider. Webhook handling và provider thực tế sẽ implement trong Phase 2.

## Migrations

```bash
cd src/Services/Payment/UrbanX.Payment.Persistence
dotnet ef migrations add <MigrationName>
```

Migration hiện có: `InitialCreate`
