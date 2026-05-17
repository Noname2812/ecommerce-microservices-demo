# Refactor Order Service — Async Ticket Flow (Final)

## Context

User có 2 vấn đề kết hợp + cleanup lớn:

### Vấn đề 1 — Flow chưa đúng + cleanup Catalog read model

**Flow chuẩn (final):**
```
Order Handler (sync, < 20ms)
 ├── FluentValidation (memory)
 ├── Check pending limit (Redis)
 ├── Publish PlaceOrderRequestedV1 { ticketId, userId, items, ... }
 └── Return 202 { ticketId }

Saga (async)
 ├── [1] Validate product/seller/price  → ICatalogServiceClient (sync HTTP + circuit breaker)
 ├── [2] Save Order { status = PROCESSING }
 ├── [3] Reserve Inventory               → soft lock
 ├── [4] Claim Coupon                    → nếu có
 ├── [5] Create Payment Session          → lưu paymentUrl, update Order → status = PENDING_PAYMENT
 └── [6] Schedule timeout 15 phút        → Saga tự quản lý (PaymentExpiry schedule)

Client polling GET /orders/ticket/{ticketId}
 ├── PROCESSING       → đang chạy saga (validate/save/reserve/coupon/payment session)
 ├── PENDING_PAYMENT  → { paymentUrl } → redirect thanh toán
 ├── CONFIRMED        → done
 └── CANCELLED        → { reason }

Payment callback
 ├── PaymentCompleted
 │    ├── Hard deduct inventory (ConfirmReservation)
 │    ├── Status = CONFIRMED
 │    └── Publish OrderConfirmedV1 → email, analytics, seller services
 │
 └── PaymentTimeoutExpired (Saga schedule)
      ├── Release inventory
      ├── Release coupon
      ├── Status = CANCELLED
      └── Publish OrderCancelledV1 → notify user
```

**Cleanup:** xoá toàn bộ Catalog snapshot read model trong Order service (model, persistence, consumers, cache, ProcessedEvent, telemetry, configs). Saga gọi trực tiếp Catalog HTTP qua `ICatalogServiceClient` (KEEP file này, thêm circuit breaker via Polly resilience).

### Vấn đề 2 — Custom Outbox thừa

Order service dùng `Shared.Outbox` chỉ để publish events. MassTransit có `AddEntityFrameworkOutbox` thay thế 1-1, atomic với SaveChanges. **Chỉ áp dụng cho Order service.**

### Decisions từ user
1. Saga tạo Order (handler chỉ publish ticket + 202)
2. Polling `GET /orders/ticket/{ticketId}` với 4 status `PROCESSING / PENDING_PAYMENT / CONFIRMED / CANCELLED`
3. Thêm `ConfirmReservation` ở Inventory (hard deduct)
4. Pending limit qua appsettings (`PlaceOrderOptions.MaxPendingPerUser`, default 1)
5. MT EF Outbox thay Shared.Outbox (Normal + Sales saga)
6. Xoá Catalog snapshot system ở Order service
7. Circuit breaker cho ICatalogServiceClient (Polly via `Microsoft.Extensions.Http.Resilience`)
8. Publish `OrderConfirmedV1` khi payment thành công

---

## Phase 1 — Order Domain refactor

### File: `src/Services/Order/UrbanX.Order.Domain/Models/OrderStatus.cs`

Thay đổi status constants:
- DROP: `Pending` (cũ)
- ADD: `Processing = "PROCESSING"`, `PendingPayment = "PENDING_PAYMENT"`
- KEEP: `Confirmed = "CONFIRMED"`, `Cancelled = "CANCELLED"`, plus các status hậu-CONFIRMED (Shipped/Delivered/Refunded) cho future logistics flow (không touch trong plan này)

### File: `src/Services/Order/UrbanX.Order.Domain/Models/Order.cs`

**1.1. `Order.Create` — đổi signature:**
- Nhận `Guid orderId` từ ngoài (saga truyền `ticketId`)
- Initial `Status = OrderStatus.Processing` (saga tạo SAU khi validate xong, TRƯỚC khi reserve)
- History entry: `prev=null, next=PROCESSING, note="Order created — validating"`

**1.2. NEW `MarkReadyForPayment(reservationId, claimId, paymentUrl, qrCodeUrl, changedById, changedByName)`:**
- Guard: `Status == Processing` (otherwise no-op)
- Atomic update:
  - `ReservationId`, `CouponClaimId` set
  - `PaymentUrl`, `QrCodeUrl` set
  - `PaymentStatus = AwaitingPayment`
  - `Status = PendingPayment`
- History entry: `prev=PROCESSING, next=PENDING_PAYMENT, note="Awaiting payment"`

**1.3. `MarkPaid` — refactor:**
```
if (Status == Cancelled) return;                                  // race: expiry compensation thắng
if (Status == Confirmed && PaymentStatus == Paid) return;         // idempotent
if (Status != PendingPayment) throw DomainException;

Status = Confirmed;
PaymentStatus = Paid;
PaymentReference = paymentSessionId;
UpdatedAt = UtcNow;
history: prev=PENDING_PAYMENT, next=CONFIRMED, note="Payment completed"
```

**1.4. `Cancel(reason, changedById, changedByName)` — guard idempotent:**
- `if (Status == Cancelled) return;`
- Otherwise: set Status=Cancelled, CancelledReason=reason, history.

**1.5. Bỏ method cũ:** `SetConfirmedWithReservation`, `SetConfirmedAsSalesOrder`, `SetPaymentSession` (gộp vào `MarkReadyForPayment`). 

**1.6. NEW `MarkSalesPayment(reservationId, claimId, paymentUrl, qrCodeUrl, campaignId, ...)`:**
- Same as MarkReadyForPayment + set `CampaignId`, `OrderType=Sales`

---

## Phase 2 — Order Handler async (return 202 trong < 20ms)

### File: `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderCommandHandler.cs`

```
public sealed class PlaceOrderCommandHandler(
    IPublishEndpoint publishEndpoint,
    IPendingOrderSlotService pendingSlots,
    IUserContext userContext)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) return Result.Failure<Guid>(OrderErrors.Forbidden);

        var slot = await pendingSlots.TryAcquireAsync(userId, ct);
        if (slot.IsFailure) return Result.Failure<Guid>(slot.Error);

        var ticketId = Guid.NewGuid();
        await publishEndpoint.Publish(new PlaceOrderRequestedV1
        {
            OrderId         = ticketId,
            UserId          = userId.ToString("D"),
            IdempotencyKey  = cmd.IdempotencyKey,
            CouponCode      = cmd.CouponCode,
            ShippingAddress = MapShippingSnapshot(cmd.ShippingAddress),
            ShippingFee     = cmd.ShippingFee,
            PricingSnapshot = cmd.PricingSnapshot,
            CustomerEmail   = userContext.Email ?? "",
            CustomerName    = userContext.FullName ?? "",
            CustomerPhone   = userContext.Phone,
            CustomerNote    = cmd.CustomerNote,
            Items           = cmd.Items.Select(i =>
                new NormalOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice)).ToList()
        }, ct);

        return Result.Success(ticketId);
    }
}
```

Bỏ inject: `IOutboxWriter`, `IProductValidator`, `IShippingValidator`, `IPricingValidator`, `ICatalogSnapshotReader`, `IOrderRepository`.

### File: `PlaceSalesOrderCommandHandler.cs` — same pattern, publish `PlaceSalesOrderRequestedV1`.

### File: `src/Services/Order/UrbanX.Order.API/Apis/OrderApis.cs`

```
private static async Task<IResult> Create(PlaceOrderCommand cmd, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(cmd, ct);
    return result.IsSuccess
        ? Results.Accepted($"{BaseURL}/ticket/{result.Value}", new { ticketId = result.Value })
        : ToOrderResult(result);
}
```

### NEW Files

**`Order.Application/Services/IPendingOrderSlotService.cs`:**
```
public interface IPendingOrderSlotService {
    Task<Result> TryAcquireAsync(Guid userId, CancellationToken ct);
    Task ReleaseAsync(Guid userId, CancellationToken ct);
}
```

**`Order.Infrastructure/Services/RedisPendingOrderSlotService.cs`:**
- Inject `ICacheService` (Lua eval) hoặc `IConnectionMultiplexer`
- Atomic Lua script: `local v = redis.call('INCR', KEYS[1]); if v == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end; return v;`
- Nếu `v > MaxPendingPerUser` → DECR (rollback) + return `Result.Failure(OrderErrors.TooManyPendingOrders)`

**`Order.Application/DependencyInjection/Options/PlaceOrderOptions.cs`:**
```
public sealed class PlaceOrderOptions {
    public const string SectionName = "PlaceOrder";
    public int MaxPendingPerUser { get; init; } = 1;
    public int PendingSlotTtlMinutes { get; init; } = 30;
}
```
Bind trong `AddApplication` + `.ValidateOnStart()`.

**`Order.Domain/Errors/OrderErrors.cs`** — thêm:
- `TooManyPendingOrders = new("Order.TooManyPending", "User has reached maximum pending orders")` → 429
- `TicketNotFound = new("Order.TicketNotFound", "Ticket not found")` → 404
- `CatalogValidationFailed(reason) = new("Order.CatalogValidationFailed", reason)` → 400
- `CatalogUnavailable = new("Order.CatalogUnavailable", "Catalog service unavailable")` → 503

### Shared.Contract — extend integration events

**`Shared.Contract/Messaging/PlaceOrder/PlaceOrderRequestedV1.cs`** — thêm:
- `ShippingAddressSnapshot ShippingAddress`
- `decimal ShippingFee`
- `string PricingSnapshot`
- `string? CustomerNote`
- `string CustomerEmail`, `string CustomerName`, `string? CustomerPhone`

Tương tự `PlaceSalesOrderRequestedV1`.

**`Shared.Contract/Messaging/Order/OrderIntegrationEvents.cs`** — thêm:
```
public record OrderConfirmedV1 : IntegrationEventBase {
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public Guid UserId { get; init; }
    public string CustomerEmail { get; init; } = "";
    public decimal FinalAmount { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }
    public IReadOnlyList<OrderItemSummary> Items { get; init; } = [];
}
```

---

## Phase 3 — Cleanup Catalog Snapshot system

### 3.1. DELETE files

**Domain:**
- `Order.Domain/ReadModels/CatalogSnapshot.cs`
- `Order.Domain/Models/ProcessedEvent.cs`
- `Order.Domain/Repositories/IProcessedEventRepository.cs`

**Application:**
- `Order.Application/ReadModels/CatalogSnapshotRow.cs`
- `Order.Application/ReadModels/ICatalogSnapshotReader.cs`
- `Order.Application/ReadModels/ICatalogSnapshotWriter.cs`
- `Order.Application/Messaging/Catalog/` — toàn bộ folder (7 file: 6 consumers + base)
- `Order.Application/Constants/CatalogProjectionConstants.cs`
- `Order.Application/Constants/SaleProjectionConstants.cs`
- `Order.Application/Abstractions/Catalog/IProductSnapshotCache.cs`
- `Order.Application/Abstractions/Promotion/ISaleSnapshotCache.cs`
- `Order.Application/Telemetry/OrderValidatorMetrics.cs` (delete; saga inline validation không cần metric riêng)

**Infrastructure:**
- `Order.Infrastructure/Services/RedisProductSnapshotCache.cs`
- `Order.Infrastructure/Services/MemoryRedisSaleSnapshotCache.cs`
- `Order.Infrastructure/Services/SaleAllocationGate.cs` + `Order.Application/Abstractions/ISaleAllocationGate.cs` (flash sale quota chuyển sang Promotion service)
- `Order.Infrastructure/DependencyInjection/Options/CatalogSnapshotOptions.cs`

**Persistence:**
- `Order.Persistence/Configurations/Read/CatalogSnapshotConfiguration.cs` (+ delete folder Read/ nếu rỗng)
- `Order.Persistence/Configurations/ProcessedEventConfiguration.cs`
- `Order.Persistence/Repositories/Read/DapperCatalogSnapshotReader.cs`
- `Order.Persistence/Repositories/Read/DapperCatalogSnapshotWriter.cs`
- `Order.Persistence/Repositories/Read/` folder (sau khi rỗng)
- `Order.Persistence/Repositories/ProcessedEventRepository.cs`

**Validators (DELETE — logic chuyển inline vào saga):**
- `Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderBusinessValidatorsImpl.cs`
- `Order.Application/Usecases/V1/Command/PlaceSalesOrder/SalePricingValidator.cs`
- `Order.Application/Usecases/V1/Command/PlaceSalesOrder/SaleEligibilityValidator.cs`

### 3.2. KEEP + extend

**`Order.Application/Clients/ICatalogServiceClient.cs`** — thêm method `Task<Result<IReadOnlyList<CatalogVariantInfo>>> GetVariantsAsync(IEnumerable<Guid> variantIds, CancellationToken ct)` trả về data đủ để validate (productId, isActive, sellerId, currentPrice, sku, name).

**`Order.Infrastructure/Services/CatalogServiceClient.cs`** — implement method mới, gọi `GET /api/v1/catalog/variants/batch?ids=...`.

### 3.3. REWRITE

**`Order.Persistence/OrderDbContext.cs`**
- `: OutboxDbContext(options)` → `: DbContext(options)` (cũng cho Phase 7)
- Bỏ `DbSet<CatalogSnapshot>`, `DbSet<ProcessedEvent>`
- Bỏ `using Shared.Outbox.EfCore;`

**`Order.Persistence/Constants/TableNames.cs`** — bỏ `catalog_snapshots`, `processed_events`.

**`Order.Persistence/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`** — bỏ register `ICatalogSnapshotReader`, `ICatalogSnapshotWriter`, `IProcessedEventRepository`.

**`Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`**:
- Bỏ register `IProductSnapshotCache`, `ISaleSnapshotCache`, `ISaleAllocationGate`
- Bỏ bind `CatalogSnapshotOptions`, `SaleSnapshotOptions`
- KEEP `HttpClient<ICatalogServiceClient>` + thêm circuit breaker:
  ```
  builder.Services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>(...)
      .AddStandardResilienceHandler(o => {
          o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
          o.CircuitBreaker.FailureRatio = 0.5;
          o.CircuitBreaker.MinimumThroughput = 10;
          o.Retry.MaxRetryAttempts = 2;
          o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
          o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
      });
  ```
- Package `Microsoft.Extensions.Http.Resilience` (centralize trong `Directory.Packages.props`)

### 3.4. `Program.cs` cleanup

- Bỏ 6 `bus.AddConsumer<ProductXxxConsumer>()` (lines 59-64)
- Bỏ `metrics.AddMeter(CatalogProjectionConstants.Metrics.MeterName)` (lines 25-26)
- Cleanup `using` directives

### 3.5. `.csproj`

- `Order.Application.csproj` — bỏ `Dapper` reference
- `Order.Persistence.csproj` — bỏ `Dapper`
- `Order.Infrastructure.csproj` — thêm `Microsoft.Extensions.Http.Resilience`

---

## Phase 4 — Saga refactor

### File: `Order.Application/Sagas/PlaceOrderNormalSagaStateMachine.cs`

**States mới (thêm vào đầu):**
- `Validating`
- `OrderPersisting`
- `InventoryReserving` (đã có)
- `CouponClaiming` (đã có)
- `PaymentSessionCreating` (NEW, gộp create + attach)
- `PaymentPending` (đã có)
- ⚠ DROP state `PromotionRedeeming` cho Normal flow (Normal không cần redeem promotion riêng — coupon claim đủ; promotion là sales-only)

**Flow:**
```
Initially:
  When(Requested)
    .Then(SnapshotRequest)
    .ThenAsync(ValidateThroughCatalogAsync)        ← gọi ICatalogServiceClient (sync HTTP + CB)
    .IfElse(saga.ValidationError != null,
        fail => fail.ThenAsync(ReleasePendingSlotAsync).TransitionTo(Faulted),
        ok   => ok
          .ThenAsync(CreateOrderProcessingAsync)   ← save Order { Status=PROCESSING }
          .Schedule(StepTimeout, ...)
          .Publish(ReserveInventoryRequestedV1)
          .TransitionTo(InventoryReserving))

During(InventoryReserving,
  When(InventoryReserved)
    .Unschedule(StepTimeout)
    .IfElse(hasCoupon,
      coupon => coupon.Schedule(StepTimeout).Publish(ClaimCouponRequestedV1).TransitionTo(CouponClaiming),
      noCoupon => noCoupon.TransitionTo(PaymentSessionCreating)),
  When(InventoryReserveFailed) → Compensating)

During(CouponClaiming,
  When(CouponClaimed) → PaymentSessionCreating,
  When(CouponClaimFailed) → publish InventoryReleaseRequestedV1 → Compensating)

WhenEnter(PaymentSessionCreating,
  .Schedule(StepTimeout)
  .Publish(CreatePaymentSessionV1))

During(PaymentSessionCreating,
  When(PaymentSessionCreated)
    .Unschedule(StepTimeout)
    .ThenAsync(MarkReadyForPaymentAsync)            ← Order: PROCESSING → PENDING_PAYMENT, attach reservation/coupon/paymentUrl
    .Schedule(PaymentExpiry, 15min)                  ← timeout 15 phút
    .TransitionTo(PaymentPending),
  When(StepTimeout.Received) → ... → Compensating)

During(PaymentPending,
  When(PaymentCompleted)
    .Unschedule(PaymentExpiry)
    .Publish(ConfirmInventoryRequestedV1)            ← hard deduct
    .ThenAsync(MarkOrderPaidAsync)                   ← Status = CONFIRMED
    .ThenAsync(PublishOrderConfirmedAsync)           ← Publish OrderConfirmedV1
    .ThenAsync(ReleasePendingSlotAsync)
    .Finalize(),

  When(PaymentExpiry.Received)
    .Publish(InventoryReleaseRequestedV1)
    .If(hasCoupon, b => b.Publish(CouponReleaseRequestedV1))
    .ThenAsync(CancelOrderAsync, "Payment expired")
    .ThenAsync(PublishOrderCancelledAsync)
    .ThenAsync(ReleasePendingSlotAsync)
    .TransitionTo(Faulted))

WhenEnter(Compensating, ...                         ← existing logic OK
    .ThenAsync(CancelOrderAsync, ...)
    .ThenAsync(PublishOrderCancelledAsync)
    .ThenAsync(ReleasePendingSlotAsync)
    .TransitionTo(Faulted))
```

**Saga methods (dùng `_scopeFactory`):**
```
private async Task ValidateThroughCatalogAsync(BehaviorContext ctx) {
    using var scope = _scopeFactory.CreateAsyncScope();
    var catalog = scope.ServiceProvider.GetRequiredService<ICatalogServiceClient>();
    var variantIds = items.Select(i => i.VariantId).Distinct();
    var result = await catalog.GetVariantsAsync(variantIds);

    if (result.IsFailure) {
        // Circuit breaker open or HTTP error
        ctx.Saga.ValidationError = result.Error.Code == "Catalog.Unavailable"
            ? "CATALOG_UNAVAILABLE"
            : "VARIANT_VALIDATION_FAILED";
        StampInstance(ctx.Saga);
        return;
    }

    // Validate: all variants active, prices match snapshot, sellers active
    var validation = ValidateBusinessRules(result.Value, items, ctx.Saga.PricingSnapshot);
    if (validation.IsFailure) {
        ctx.Saga.ValidationError = validation.Error.Code;
        StampInstance(ctx.Saga);
    }
}

private async Task CreateOrderProcessingAsync(BehaviorContext ctx) {
    using var scope = _scopeFactory.CreateAsyncScope();
    var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    await uow.ExecuteInTransactionAsync(async () => {
        var order = OrderFactory.BuildFromSaga(ctx.Saga, ctx.Saga.OrderId);   // Status = PROCESSING
        repo.Add(order);
    });
}

private async Task MarkReadyForPaymentAsync(ctx, sessionMsg) {
    // Atomic: PROCESSING → PENDING_PAYMENT + attach reservation/coupon/paymentUrl
    await using var scope = _scopeFactory.CreateAsyncScope();
    var repo = ...; var uow = ...;
    await uow.ExecuteInTransactionAsync(async () => {
        var order = await repo.GetByIdAsync(saga.OrderId);
        order?.MarkReadyForPayment(
            saga.ReservationId!.Value, saga.CouponClaimId,
            sessionMsg.PaymentUrl, sessionMsg.QrCodeUrl,
            userId, "");
    });
    saga.PaymentSessionId = sessionMsg.PaymentSessionId;
    StampInstance(saga);
}

private async Task MarkOrderPaidAsync(saga, sessionId) { ... order.MarkPaid(...) }
private async Task CancelOrderAsync(saga, reason) { ... }
private async Task ReleasePendingSlotAsync(saga) {
    using var scope = _scopeFactory.CreateAsyncScope();
    var slots = scope.ServiceProvider.GetRequiredService<IPendingOrderSlotService>();
    await slots.ReleaseAsync(Guid.Parse(saga.UserId), CancellationToken.None);
}

private async Task PublishOrderConfirmedAsync(BehaviorContext ctx) {
    using var scope = _scopeFactory.CreateAsyncScope();
    var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
    var repo      = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var order     = await repo.GetByIdAsync(ctx.Saga.OrderId);
    if (order is null) return;
    await publisher.Publish(new OrderConfirmedV1 {
        OrderId       = order.Id, OrderNumber = order.OrderNumber,
        UserId        = order.UserId, CustomerEmail = order.CustomerEmail,
        FinalAmount   = order.FinalAmount, ConfirmedAt = DateTimeOffset.UtcNow,
        Items         = order.Items.Select(i => new OrderItemSummary(...)).ToList()
    });
}

private async Task PublishOrderCancelledAsync(...) { ... }
```

### File: `PlaceOrderNormalSagaState.cs` — thêm fields

```
public string? ShippingAddressJson { get; set; }
public decimal ShippingFee { get; set; }
public string PricingSnapshot { get; set; } = "{}";
public string CustomerEmail { get; set; } = "";
public string CustomerName { get; set; } = "";
public string? CustomerPhone { get; set; }
public string? CustomerNote { get; set; }
public string? ValidationError { get; set; }
public string? PaymentUrl { get; set; }       // saga cache để polling không phải JOIN Order
public string? QrCodeUrl { get; set; }
public DateTimeOffset? PaymentExpiresAt { get; set; }
```

### File: `PlaceSalesOrderSagaStateMachine.cs` + state — apply pattern
- Thêm `Validating`, `OrderPersisting`, `PaymentSessionCreating` states
- `Confirming` state đã có → đổi semantic gọi `MarkSalesPayment`
- Bỏ `SaleAllocationGate` references (đã delete) — quota check chuyển hoàn toàn sang Promotion service qua `RedeemSalePromotionRequestedV1`
- Saga state thêm fields tương tự

### File: `OrderFactory.cs` — refactor
- New signature: `BuildFromSaga(PlaceOrderNormalSagaState saga, Guid orderId)`
- Đọc Items từ `saga.ItemsJson` (đã có)
- Build Order entity với `Status=Processing` (Order.Create signature mới)

---

## Phase 5 — Inventory: ConfirmReservation

### Domain
**`InventoryReservation.cs`** — `Confirm(DateTimeOffset utcNow)`: guard Status==Pending → Status=Confirmed, ConfirmedAt=utcNow.
**`InventoryItem.cs`** — `ConfirmDeduction(int quantity, DateTimeOffset utcNow)`: guard quantity<=QuantityReserved → QuantityReserved -= qty, QuantityOnHand -= qty.

### Application
**NEW `Inventory.Application/Usecases/V1/Command/ConfirmReservation/`** — `ConfirmReservationCommand` + Validator + Handler. Idempotent: nếu Reservation.Status=Confirmed → return Success. Marker `IConcurrencyRetriableCommand`.

**NEW `Inventory.Application/Messaging/PlaceOrderSaga/ConfirmInventoryRequestedConsumer.cs`** — consume `ConfirmInventoryRequestedV1` → send command.

### Contract
**`Shared.Contract/Messaging/PlaceOrderSaga/InventoryEvents.cs`** — thêm:
```
public record ConfirmInventoryRequestedV1 : IntegrationEventBase {
    public Guid OrderId { get; init; }
    public Guid ReservationId { get; init; }
    public string IdempotencyKey { get; init; }
}
```

### `Inventory.API/Program.cs` — `bus.AddConsumer<ConfirmInventoryRequestedConsumer>()`.

### Migration: `dotnet ef migrations add AddReservationConfirmedAt` — column `confirmed_at` nullable.

---

## Phase 6 — GET /orders/ticket/{ticketId}

### NEW `Order.Application/Usecases/V1/Query/GetOrderByTicket/`

```
[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record GetOrderByTicketQuery(Guid TicketId) : IQuery<OrderTicketStatusDto>;

public record OrderTicketStatusDto(
    Guid TicketId,
    string Status,                  // PROCESSING | PENDING_PAYMENT | CONFIRMED | CANCELLED
    Guid? OrderId,
    string? PaymentUrl,
    string? QrCodeUrl,
    string? PaymentStatus,
    string? CancelledReason,
    DateTimeOffset? PaymentExpiresAt
);
```

**Handler:**
1. Query Order theo `Id = ticketId` (tickedId == orderId):
   - **Tồn tại** → trả `{ Status=order.Status, OrderId, PaymentUrl, QrCodeUrl, PaymentStatus, CancelledReason, PaymentExpiresAt từ saga }`
   - **Không tồn tại** → query saga state qua `OrderDbContext.Set<PlaceOrderNormalSagaState>()` hoặc `PlaceSalesOrderSagaState` theo `CorrelationId = ticketId`:
     - Saga tồn tại + chưa Faulted → `Status="PROCESSING"`
     - Saga Faulted → `Status="CANCELLED"` + `CancelledReason=saga.FailureReason`
     - Cả 2 không thấy → `Result.Failure(OrderErrors.TicketNotFound)` → 404
2. Authorize: chỉ owner hoặc admin được xem (check `order.UserId == userContext.UserId` hoặc role Admin)

### `Order.API/Apis/OrderApis.cs`
```
v1.MapGet("/ticket/{ticketId:guid}", async (Guid ticketId, ISender sender, CancellationToken ct) => {
    var result = await sender.Send(new GetOrderByTicketQuery(ticketId), ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ToOrderResult(result);
});
```

---

## Phase 7 — Thay Shared.Outbox bằng MassTransit EF Outbox

### 7.1. `OrderDbContext.cs`
- `: OutboxDbContext(options)` → `: DbContext(options)` (đã include trong Phase 3.3)
- Bỏ `using Shared.Outbox.EfCore;`

### 7.2. Handlers replacing IOutboxWriter
- `PlaceOrderCommandHandler.cs`, `PlaceSalesOrderCommandHandler.cs` — đã rewrite ở Phase 2 dùng `IPublishEndpoint`.
- `CancelOrderCommandHandler.cs`:
  - Inject `IPublishEndpoint`
  - Publish: `OrderCancelledV1` (+ `InventoryReleaseRequestedV1` nếu `order.ReservationId.HasValue` + `CouponReleaseRequestedV1` nếu `order.CouponClaimId.HasValue`)

### 7.3. `Program.cs`

Remove:
```
builder.Services.AddOutbox<OrderDbContext>(configureDb: null, builder.Configuration);
builder.Services.AddCompensationOutbox(builder.Configuration);
```

Thêm trong `AddMessaging(..., configureBus: bus => {...})`:
```
bus.AddEntityFrameworkOutbox<OrderDbContext>(o => {
    o.UsePostgres();
    o.UseBusOutbox();
    o.QueryDelay = TimeSpan.FromSeconds(1);
    o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
});
```
⚠ `bus.AddEntityFrameworkOutbox`, không phải `services.AddEntityFrameworkOutbox`. MT tự register `BusOutboxDeliveryService` IHostedService.

### 7.4. `.csproj` bỏ Shared.Outbox: Order.Application, Order.Persistence, Order.API.

### 7.5. Bỏ `using Shared.Outbox.*` toàn bộ Order service.

---

## Phase 8 — Migration tổng hợp

```
cd src/Services/Order/UrbanX.Order.Persistence
dotnet ef migrations add CleanupCatalogSnapshotAndOutboxRefactor
dotnet ef database update

cd src/Services/Inventory/UrbanX.Inventory.Persistence
dotnet ef migrations add AddReservationConfirmedAt
dotnet ef database update
```

**Order migration:**
- DROP tables: `read.catalog_snapshots`, `processed_events`, `outbox_messages`, `outbox_processed_events`, `compensation_outbox_messages`
- DROP schema `read` (nếu rỗng sau drop)
- ADD saga state fields trên cả 2 saga state tables (ShippingAddressJson, ShippingFee, PricingSnapshot, CustomerEmail/Name/Phone/Note, ValidationError, PaymentUrl, QrCodeUrl, PaymentExpiresAt)
- ADD MT outbox tables: `inbox_state`, `outbox_state`, `outbox_message`
  - Override snake_case nếu cần: `builder.Entity<InboxState>().ToTable("inbox_state");` (similar cho OutboxState/OutboxMessage)

**Inventory migration:** ADD column `confirmed_at` (timestamp nullable) trên `inventory_reservations`.

---

## Race condition strategy

| Scenario | Mitigation |
|---|---|
| PaymentExpiry vs PaymentCompleted (saga) | `ConcurrencyMode.Optimistic` (Program.cs:47) — 1 thắng, kia retry, state khác → no-op |
| MarkPaid sau khi Cancel commit | Domain guard `if (Status == Cancelled) return;` |
| PaymentSessionCreated sau khi đã Faulted | `Status != Processing` → MarkReadyForPayment no-op |
| ConfirmInventory + Cancel race | ConfirmReservation idempotent check `Status == Confirmed` → skip |
| ConfirmReservation gọi 2 lần (retry) | `IConcurrencyRetriableCommand` + idempotent guard |
| User race PlaceOrder 2 ticket | Redis pending slot — atomic Lua INCR + EXPIRE |
| Saga Faulted nhưng slot chưa release | Release slot trong Compensating + Faulted entry (`WhenEnter(Faulted, b => b.ThenAsync(ReleasePendingSlot))`) |
| Catalog service down | Circuit breaker (Polly Resilience) → `Catalog.Unavailable` error → saga Faulted + slot release |

---

## Idempotency strategy (chi tiết)

Phân tán = duplicate event là chuyện thường xuyên: MassTransit redelivery, MT EF Outbox publish retry, RabbitMQ at-least-once, saga retry trên concurrency conflict, user double-click. Mỗi nơi xử lý event đều phải idempotent.

### 1. Infrastructure layer (MassTransit + Outbox)

| Cơ chế | Áp dụng |
|---|---|
| **MT EF Outbox `DuplicateDetectionWindow=10min`** | MT tự skip publish duplicate (cùng `MessageId`) trong 10 phút |
| **MT Inbox table** | Consumer tự dedupe event đã consume (cùng `MessageId`) — auto khi dùng `bus.AddEntityFrameworkOutbox` |
| **Saga `CorrelateById(OrderId)`** | 1 event chỉ match 1 saga instance; duplicate event → saga load lại → state machine check `During(...)` filter |
| **HttpIdempotency middleware (đã có)** | Order.API line 27 `AddHttpIdempotency(ServiceId="order")` — request-level cache theo header `Idempotency-Key`; cùng key trả cached response (cùng ticketId), không tạo saga mới |
| **RabbitMQ at-least-once + MT retry** | Per-consumer cấu hình retry exponential (KHÔNG bật bus-wide theo shared-rules), max 3 lần |

### 2. Domain layer (guards idempotent)

| Method | Guard |
|---|---|
| `Order.MarkReadyForPayment` | `if (Status != Processing) return;` — đã được set PENDING_PAYMENT thì skip |
| `Order.MarkPaid` | 3 guards: `Status == Cancelled` → return; `Status == Confirmed && PaymentStatus == Paid` → return (true idempotent); `Status != PendingPayment` → throw (state invariant) |
| `Order.Cancel` | `if (Status == Cancelled) return;` — không add history trùng, không re-publish |
| `InventoryReservation.Confirm` | `if (Status == Confirmed) return;` — không double-set ConfirmedAt |
| `InventoryItem.ConfirmDeduction` | Được wrap trong cùng TX với `Confirm` (atomic). Nếu Confirm idempotent skip → method này không gọi. Vẫn thêm guard `if (quantity > QuantityReserved) throw` để chống corrupted state |
| `InventoryReservation.MarkReleased` | `if (Status == Released) return;` (đã có) |

### 3. Application layer (handlers)

| Handler | Idempotency strategy |
|---|---|
| `PlaceOrderCommandHandler` | (a) HttpIdempotency middleware (request level); (b) `cmd.IdempotencyKey` được publish vào event — saga dùng làm sub-idempotency-key cho inventory (`{key}:inv`), coupon (`{key}:cpn`), payment (`{key}:pay`); (c) Redis pending slot: nếu user double-click → slot 2 reject 429 |
| `CancelOrderCommandHandler` | Domain `Cancel` idempotent. Publish `OrderCancelledV1` — outbox dedupe theo MessageId. Inventory release publish dùng `ReservationId` làm dedup key |
| `ConfirmReservationCommandHandler` (Inventory) | Marker `IConcurrencyRetriableCommand` (PostgreSQL `xmin` shadow property handle optimistic concurrency). Idempotent: load Reservation → nếu `Status == Confirmed` → `return Result.Success()` ngay, không double-deduct |
| `ReleaseReservationCommandHandler` (Inventory) | Đã có; idempotent nếu `Status == Released` |
| `ReserveInventoryCommandHandler` (Inventory) | Đã có; dedupe theo `OrderIdempotencyKey` (`{key}:inv`) — nếu reservation tồn tại với cùng key → return existing ReservationId |

### 4. Saga state transitions

- **Validating duplicate trigger**: `Initially(When(Requested))` — saga đã exist → `Requested` event không match (`Initially` filter). MT log warning, skip.
- **InventoryReserved arrive 2 lần**: saga đang ở `InventoryReserving` → handle event lần 1 → transition CouponClaiming/PaymentSessionCreating. Event lần 2 arrive → `During(InventoryReserving)` không còn match → MassTransit log "no match", skip.
- **Saga concurrent retry (optimistic concurrency conflict)**: MT auto-retry với policy mặc định (3 lần). Sau retry vẫn fail → message vào _error queue.
- **`Schedule(...)` duplicate fire**: MT scheduler dùng `tokenId` để track. `Unschedule` set tokenId=null. Nếu event fire trước Unschedule kịp → saga handler check state → no-op.

### 5. Redis pending slot Lua scripts

**`TryAcquire`** (atomic):
```lua
local current = redis.call('INCR', KEYS[1])
if current == 1 then
    redis.call('EXPIRE', KEYS[1], ARGV[1])    -- TTL only on first increment
end
if current > tonumber(ARGV[2]) then
    redis.call('DECR', KEYS[1])               -- rollback nếu vượt limit
    return 0
end
return current
```

**`Release`** (atomic, không underflow):
```lua
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
if current <= 0 then return 0 end
return redis.call('DECR', KEYS[1])
```
→ Release gọi 2 lần (saga compensating + handler cancel) chỉ decrement 1 lần thật sự.

### 6. Publish events từ saga (`OrderConfirmedV1`, `OrderCancelledV1`)

- Saga publish qua `IPublishEndpoint` → MT EF Outbox stage → publish vào bus
- DuplicateDetectionWindow 10 phút: 2 lần publish cùng MessageId → 1 lần đến downstream
- **Saga set explicit MessageId** = deterministic (hash của `saga.OrderId + eventType`) để đảm bảo dedup hoạt động qua nhiều saga retry:
  ```
  await publisher.Publish<OrderConfirmedV1>(msg, ctx => {
      ctx.MessageId = DeterministicGuid($"order-confirmed:{order.Id}");
  });
  ```

### 7. Downstream consumers (out of scope nhưng note)

- Email/analytics/seller consumers SUBSCRIBE `OrderConfirmedV1`/`OrderCancelledV1` — phải tự idempotent qua MT Inbox + business key (OrderId)
- Plan này không implement consumers; chỉ publish + document yêu cầu idempotency.

### 8. Test checklist idempotency

- [ ] POST PlaceOrder cùng `Idempotency-Key` 2 lần → cùng ticketId, chỉ 1 saga + 1 Order
- [ ] Mock publish `PaymentSessionCompletedV1` 2 lần → Order CONFIRMED 1 lần, Inventory deduct 1 lần, `OrderConfirmedV1` publish 1 lần (dedup window)
- [ ] Mock publish `PaymentExpiry` + `PaymentCompleted` đồng thời → exactly 1 outcome
- [ ] Cancel order đã CANCELLED → no-op (Result.Success, không re-publish)
- [ ] Inventory `ConfirmReservationCommand` gọi 2 lần với cùng ReservationId → QuantityOnHand giảm 1 lần
- [ ] Redis pending slot: TryAcquire → Release → Release lại → slot=0 (không âm)
- [ ] Saga thrash retry (force concurrency conflict) → eventual consistency, không duplicate side-effect

---

## Critical Files

### KEEP (extend)
- `Order.Application/Clients/ICatalogServiceClient.cs` + `Order.Infrastructure/Services/CatalogServiceClient.cs` — primary source + Polly resilience
- `Order.API/Middleware/PlaceOrderRateLimitMiddleware.cs` — verify còn cần (có overlap với `IPendingOrderSlotService`); plan default: bỏ middleware, dùng service inline trong handler

### DELETE — xem chi tiết Phase 3.1 + validators ở Phase 3 (~25 files)

### REWRITE
- `Order.Domain/Models/Order.cs` (Phase 1)
- `Order.Domain/Models/OrderStatus.cs` (Phase 1)
- `Order.Persistence/OrderDbContext.cs` (Phase 3.3, 7.1)
- `Order.Persistence/Constants/TableNames.cs` (Phase 3.3)
- `Order.Persistence/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` (Phase 3.3)
- `Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` (Phase 3.3)
- `Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderCommandHandler.cs` (Phase 2)
- `Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs` (Phase 2)
- `Order.Application/Usecases/V1/Command/CancelOrder/CancelOrderCommandHandler.cs` (Phase 7.2)
- `Order.Application/Sagas/PlaceOrderNormalSagaStateMachine.cs` + state (Phase 4)
- `Order.Application/Sagas/PlaceSalesOrderSagaStateMachine.cs` + state (Phase 4)
- `Order.Application/Usecases/V1/Command/Common/OrderFactory.cs` (Phase 4)
- `Order.API/Program.cs` (Phase 3.4, 7.3)
- `Order.API/Apis/OrderApis.cs` (Phase 2, 6)
- `Order.API/appsettings.json` — thêm `PlaceOrder` + chỉnh `OrderPayment:NormalOrderExpiryMinutes=15` cho consistent
- Shared.Contract: `PlaceOrderRequestedV1.cs`, `PlaceSalesOrderRequestedV1.cs`, `InventoryEvents.cs` (+ ConfirmInventoryRequestedV1), `OrderIntegrationEvents.cs` (+ OrderConfirmedV1)
- Inventory: `InventoryReservation.cs`, `InventoryItem.cs`, `Inventory.API/Program.cs`

### NEW
- `Order.Application/Services/IPendingOrderSlotService.cs`
- `Order.Infrastructure/Services/RedisPendingOrderSlotService.cs`
- `Order.Application/DependencyInjection/Options/PlaceOrderOptions.cs`
- `Order.Application/Usecases/V1/Query/GetOrderByTicket/*` (Query + Validator + Handler + Dto)
- `Inventory.Application/Usecases/V1/Command/ConfirmReservation/*` (Command + Validator + Handler)
- `Inventory.Application/Messaging/PlaceOrderSaga/ConfirmInventoryRequestedConsumer.cs`
- `docs/order/async-ticket-flow.md`
- `docs/order/reserve-vs-deduct.md`

---

## Verification

### Build + Migration
```
rtk err dotnet build UrbanX.sln
cd src/Services/Order/UrbanX.Order.Persistence && dotnet ef database update
cd src/Services/Inventory/UrbanX.Inventory.Persistence && dotnet ef database update
```
Verify Postgres: dropped `read.catalog_snapshots`, `processed_events`, custom outbox tables; added MT outbox tables + saga state columns + `inventory_reservations.confirmed_at`.

### E2E Normal Order — happy path
1. `POST /api/v1/order/orders` → **202** `{ ticketId }` trong < 100ms
2. `GET /api/v1/order/orders/ticket/{ticketId}` poll 1s/lần:
   - T+0..2s: `Status="PROCESSING"` (saga validating qua Catalog HTTP)
   - T+2..4s: `Status="PROCESSING"` (saga đang reserve / claim coupon)
   - T+4..6s: `Status="PENDING_PAYMENT"` + `paymentUrl` + `paymentExpiresAt` (15 phút sau)
3. DB Order: Id=ticketId, Status=PENDING_PAYMENT, ReservationId set, history 2 entries (Processing → PendingPayment)
4. Mock publish `PaymentSessionCompletedV1`:
   - Order Status=CONFIRMED, PaymentStatus=Paid
   - Inventory QuantityOnHand giảm, reservation Status=Confirmed (hard deduct)
   - RabbitMQ publish `OrderConfirmedV1` (verify trên Aspire RabbitMQ tab)
   - Redis pending slot decrement
5. Status history 3 entries: Processing → PendingPayment → Confirmed

### E2E Timeout
1. PlaceOrder + reserved (set `OrderPayment:NormalOrderExpiryMinutes=1` trong `appsettings.Development.json`)
2. Wait > 1 phút
3. Order Status=CANCELLED, Inventory QuantityReserved decrement, coupon released, slot decrement
4. RabbitMQ publish `OrderCancelledV1`

### E2E Catalog validation failure
1. PlaceOrder với variantId không tồn tại
2. Saga `Validating` → HTTP 404 → `saga.ValidationError = "VARIANT_VALIDATION_FAILED"` → Faulted
3. GET ticket → `Status="CANCELLED"`, `CancelledReason="VARIANT_VALIDATION_FAILED"`
4. KHÔNG có Order entry trong DB; Redis slot decrement

### E2E Catalog unavailable (circuit breaker)
1. Stop Catalog service
2. PlaceOrder 10 lần liên tiếp → CB open sau 5 fails
3. Polling tickets: tất cả `Status="CANCELLED"`, `Reason="CATALOG_UNAVAILABLE"`
4. Slot decrement đầy đủ

### E2E Pending limit
1. PlaceOrder 1 — 202
2. PlaceOrder 2 ngay sau (saga 1 chưa xong) — **429** `Order.TooManyPending`
3. Cancel order 1 → slot release → PlaceOrder 3 OK

### E2E Cancel sau reserve
1. PlaceOrder → reserved (PENDING_PAYMENT)
2. POST cancel → DB Status=CANCELLED, MT publish `OrderCancelledV1` + `InventoryReleaseRequestedV1` + (nếu coupon) `CouponReleaseRequestedV1`
3. Inventory release OK

### Race smoke test
- Integration test: schedule PaymentExpiry=1s, fire PaymentCompleted gần đồng thời
- Verify: exactly 1 outcome (CONFIRMED/CANCELLED), history không duplicate, slot release đúng 1 lần, OrderConfirmedV1 XOR OrderCancelledV1 (chứ không cả 2)

### MT Outbox verification
- Postgres `outbox_message` có rows trong khi saga publish, purge sau publish thành công
- Aspire RabbitMQ: events đến đúng theo thứ tự, không duplicate

---

## Out of Scope

- KHÔNG đổi Shared.Outbox ở Catalog/Identity/Payment service
- KHÔNG refactor Payment service flow nội bộ (chỉ consume `CreatePaymentSessionV1`, publish `PaymentSessionCompletedV1` như hiện tại)
- KHÔNG refactor Promotion service (saga vẫn publish `RedeemSalePromotionRequestedV1` cho Sales)
- KHÔNG migrate data từ bảng cũ (dev project, drop được)
- Status hậu-CONFIRMED (SHIPPED, DELIVERED, REFUND_REQUESTED, REFUNDED) — giữ trong enum cho future, plan này không touch
- `OrderConfirmedV1`/`OrderCancelledV1` consumers (email, analytics, seller) — out of scope; plan chỉ publish event, các service downstream subscribe sau
