# TASK-07 · PlaceOrder Normal: Sync → Async + Payment Expiry

| | |
|---|---|
| **Effort** | ~2 ngày |
| **Depends on** | — (parallel) |
| **Blocks** | TASK-08 (docs) |
| **Branch** | `feat/saga/task-07-normal-async` |

## Goal

1. Chuyển PlaceOrder normal từ **sync orchestration** sang **async (202 Accepted)** — đồng nhất với PlaceSalesOrder.
2. Thêm **payment step** cho cả normal và sales: sau khi CONFIRMED → Payment service tạo URL/QR → chờ `PaymentCompletedV1`.
3. Order **CONFIRMED tự động expire** nếu không nhận payment success:
   - Normal order: **30 phút** (configurable)
   - Sales order: **15 phút** (configurable)
4. Timeout values lấy từ `appsettings.json` qua Options pattern.

---

## Context

### Current flow (normal — sync, problematic)

```
Client → POST /orders  →  PlaceOrderCommandHandler:
  ├── Validate (parallel)
  ├── HTTP: Promotion.RedeemAsync()    ← sync block, infinite timeout
  ├── HTTP: Inventory.ReserveAsync()   ← sync block
  ├── HTTP: Coupon.ClaimAsync()        ← sync block
  └── SaveChanges (CONFIRMED) → 200 OK
```

### Target flow (normal — async + payment)

```
Client → POST /orders → PlaceOrderCommandHandler:
  ├── Validate (parallel: product / shipping / pricing)
  ├── SaveChanges (PENDING)
  └── Outbox: PlaceOrderRequestedV1 → 202 Accepted { orderId, status:"Pending" }

PlaceOrderNormalSaga:
  Initial
    ├── [couponCode?] → RedeemPromotionForNormalOrderRequestedV1 → PromotionRedeeming
    └── [no coupon]  → ReserveInventoryRequestedV1              → InventoryReserving

  PromotionRedeeming
    ├── NormalOrderPromotionRedeemedV1  → InventoryReserving
    └── NormalOrderPromotionRedeemFailedV1 / Timeout → Compensating

  InventoryReserving
    ├── InventoryReservedV1  → [coupon?] CouponClaiming | PaymentPending (confirm + initiate payment)
    └── InventoryReserveFailedV1 / Timeout → Compensating

  CouponClaiming
    ├── CouponClaimedV1   → PaymentPending (confirm + initiate payment)
    └── CouponClaimFailedV1 / Timeout → Compensating (release inventory)

  PaymentPending                            ← ORDER = CONFIRMED ở đây
    ├── CreatePaymentSessionV1 published   → Payment service tạo URL/QR
    ├── PaymentSessionCreatedV1 received   → store paymentUrl/qrCode on Order
    ├── PaymentCompletedV1 received        → MarkPaid → Finalized
    └── PaymentExpiryTimeout (30 phút)     → Compensating (release inventory + coupon)

  Compensating
    └── release inventory → [coupon?] release coupon → Cancel order → Faulted

  Finalized  (saga row deleted from DB)
  Faulted    (saga row kept for audit)
```

### Target flow (sales — additions to existing PlaceSalesOrderSaga)

```diff
  PaymentProcessing (giữ nguyên — xử lý charge/pre-auth nếu cần)
    ├── PaymentProcessed   → Confirming
    └── PaymentFailed / Timeout → Compensating

  Confirming (hiện tại: Finalize ngay — SẼ ĐỔI)
-   WhenEnter → Finalize()
+   WhenEnter → Confirm order + publish CreatePaymentSessionV1 → PaymentPending

  PaymentPending  ← MỚI
    ├── PaymentSessionCreatedV1  → store URL/QR on Order
    ├── PaymentCompletedV1       → MarkPaid → Finalized
    └── PaymentExpiryTimeout (15 phút) → Compensating (release all + Cancel)
```

---

## Files

### New contracts — `Shared.Contract`

| File | Namespace | Mục đích |
|---|---|---|
| `Messaging/PlaceOrder/PlaceOrderRequestedV1.cs` | `Shared.Contract.Messaging.PlaceOrder` | Trigger saga normal từ handler |
| `Messaging/PlaceOrder/NormalOrderPromotionEvents.cs` | `Shared.Contract.Messaging.PlaceOrder` | `RedeemPromotionForNormalOrderRequestedV1`, `NormalOrderPromotionRedeemedV1`, `NormalOrderPromotionRedeemFailedV1` |
| `Messaging/Payment/CreatePaymentSessionV1.cs` | `Shared.Contract.Messaging.Payment` | Order saga → Payment service: tạo checkout session |
| `Messaging/Payment/PaymentSessionCreatedV1.cs` | `Shared.Contract.Messaging.Payment` | Payment → Order saga: trả URL + QR |
| `Messaging/Payment/PaymentCompletedV1.cs` | `Shared.Contract.Messaging.Payment` | Payment → Order saga: user đã trả |
| `Messaging/Payment/PaymentExpiryTimeoutV1.cs` | `Shared.Contract.Messaging.Payment` | MassTransit scheduled timeout message |

> **Reuse** từ `PlaceOrderSaga` namespace (không tạo mới):
> `ReserveInventoryRequestedV1` · `InventoryReservedV1` · `InventoryReserveFailedV1`
> `ClaimCouponRequestedV1` · `CouponClaimedV1` · `CouponClaimFailedV1`
> `InventoryReleaseRequestedV1` · `CouponReleaseRequestedV1`

### New / Modified — `Order` service

| File | Thay đổi |
|---|---|
| `Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderCommandHandler.cs` | **Xoá** sync HTTP; chỉ validate + PENDING + publish `PlaceOrderRequestedV1` |
| `Order.Application/Sagas/PlaceOrderNormalSagaState.cs` | **Mới** — state cho normal saga |
| `Order.Application/Sagas/PlaceOrderNormalSagaStateMachine.cs` | **Mới** — full state machine |
| `Order.Application/Sagas/PlaceSalesOrderSagaStateMachine.cs` | **Sửa** — đổi `Confirming` → thêm `PaymentPending` state, schedule 15min timer |
| `Order.Application/Sagas/PlaceSalesOrderSagaState.cs` | **Sửa** — thêm `PaymentExpiryTokenId` |
| `Order.Domain/Models/Order.cs` | **Thêm** `MarkPaid()`, `MarkPaymentExpired()`, `PaymentUrl`, `QrCodeUrl` |
| `Order.Domain/Models/PaymentStatus.cs` | **Thêm** `AwaitingPayment = "AWAITING_PAYMENT"` |
| `Order.Application/DependencyInjection/Options/OrderPaymentOptions.cs` | **Mới** — config class |
| `Order.API/appsettings.json` | **Thêm** `Order:Payment` section |
| `Order.API/Apis/OrderApis.cs` | **Sửa** `PlaceOrderV1` → 202 Accepted |

### Removed

| File | Lý do |
|---|---|
| `Order.Application/PlaceOrderCompensationContext.cs` | Compensation chuyển vào saga |
| `Order.Infrastructure/.../CompensationCollector.cs` | Không còn cần |

---

## Implementation

### 1. Config — `OrderPaymentOptions`

```csharp
// Order.Application/DependencyInjection/Options/OrderPaymentOptions.cs
public sealed class OrderPaymentOptions
{
    public const string SectionName = "Order:Payment";

    public int NormalOrderExpiryMinutes { get; init; } = 30;
    public int SalesOrderExpiryMinutes  { get; init; } = 15;
}
```

```json
// Order.API/appsettings.json
{
  "Order": {
    "Payment": {
      "NormalOrderExpiryMinutes": 30,
      "SalesOrderExpiryMinutes": 15
    }
  }
}
```

```csharp
// AddApplication() hoặc Program.cs
builder.Services.AddOptions<OrderPaymentOptions>()
    .BindConfiguration(OrderPaymentOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Inject `IOptions<OrderPaymentOptions>` vào saga constructor.

---

### 2. New payment contracts

```csharp
// Shared.Contract/Messaging/Payment/CreatePaymentSessionV1.cs
namespace Shared.Contract.Messaging.Payment;

public record CreatePaymentSessionV1 : IntegrationEventBase
{
    public override string Source => "order-service";

    public required Guid OrderId { get; init; }
    public required string IdempotencyKey { get; init; }  // "{orderId}:pay"
    public required decimal Amount { get; init; }
    public required string Currency { get; init; } = "VND";
    public string? OrderNumber { get; init; }
}

// PaymentSessionCreatedV1.cs
public record PaymentSessionCreatedV1 : IntegrationEventBase
{
    public override string Source => "payment-service";

    public required Guid OrderId { get; init; }
    public required string PaymentSessionId { get; init; }
    public required string PaymentUrl { get; init; }
    public string? QrCodeUrl { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

// PaymentCompletedV1.cs
public record PaymentCompletedV1 : IntegrationEventBase
{
    public override string Source => "payment-service";

    public required Guid OrderId { get; init; }
    public required string PaymentSessionId { get; init; }
    public required decimal AmountPaid { get; init; }
    public required DateTimeOffset PaidAt { get; init; }
}

// PaymentExpiryTimeoutV1.cs — scheduled message
public record PaymentExpiryTimeoutV1
{
    public Guid OrderId { get; init; }
}
```

---

### 3. Order domain additions

```csharp
// Order.cs — thêm fields
public string? PaymentUrl  { get; private set; }
public string? QrCodeUrl   { get; private set; }

// Gọi khi PaymentSessionCreatedV1 nhận được (có thể qua consumer riêng hoặc trong saga activity)
public void SetPaymentSession(string paymentUrl, string? qrCodeUrl)
{
    PaymentUrl = paymentUrl;
    QrCodeUrl  = qrCodeUrl;
    PaymentStatus = Models.PaymentStatus.AwaitingPayment;
    UpdatedAt = DateTimeOffset.UtcNow;
}

// Gọi khi PaymentCompletedV1 nhận được
public void MarkPaid(string paymentSessionId, Guid changedById, string changedByName)
{
    var prev = PaymentStatus;
    PaymentStatus    = Models.PaymentStatus.Paid;
    PaymentReference = paymentSessionId;
    UpdatedAt        = DateTimeOffset.UtcNow;
    _statusHistory.Add(OrderStatusHistory.Create(
        Id, Status, Status, "Payment completed", changedById, changedByName));
}

// Gọi khi payment timeout — dùng Cancel() hiện có với reason rõ ràng
// Cancel(reason: "Payment expired", ...) → Status = CANCELLED
```

```csharp
// PaymentStatus.cs — thêm
public const string AwaitingPayment = "AWAITING_PAYMENT";
```

---

### 4. PlaceOrderCommandHandler (simplified)

```csharp
// Xoá: IPromotionServiceClient, IInventoryClient, ICouponClient, CompensationContext
public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext,
    IProductValidator productValidator,
    IShippingValidator shippingValidator,
    IPricingValidator pricingValidator)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userId is null || userId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var validation = await ValidateBusinessRulesAsync(cmd, ..., ct);
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var order = OrderEntity.Create(
            GenerateOrderNumber(), userId.Value,
            cmd.CustomerEmail?.Trim() ?? string.Empty,
            cmd.ShippingAddress.FullName, cmd.ShippingAddress.Phone,
            ShippingAddress.Create(...),
            cmd.ShippingFee, cmd.CouponCode,
            couponDiscount: 0m,     // saga cập nhật sau promotion redeem
            cmd.CustomerNote, cmd.IdempotencyKey, specs);
        // Status = PENDING (default)

        orderRepository.Add(order);

        await outboxWriter.WriteAsync(new PlaceOrderRequestedV1
        {
            OrderId        = order.Id,
            UserId         = userId.Value.ToString("D"),
            IdempotencyKey = cmd.IdempotencyKey,
            CouponCode     = cmd.CouponCode,
            Subtotal       = order.Subtotal,
            ShippingFee    = order.ShippingFee,
            Items          = order.Items.Select(i =>
                new NormalOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice)).ToList()
        }, ct);

        return Result.Success(order.Id);
    }
}
```

---

### 5. PlaceOrderNormalSagaState

```csharp
// Application/Sagas/PlaceOrderNormalSagaState.cs
public sealed class PlaceOrderNormalSagaState : SagaStateBase
{
    // Inherited: CorrelationId (= OrderId), CurrentState, CreatedAt, UpdatedAt, Version

    public Guid OrderId { get; set; }
    public string UserId { get; set; } = default!;
    public string IdempotencyKey { get; set; } = default!;
    public string? CouponCode { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal PromotionDiscount { get; set; }
    public decimal CouponDiscount { get; set; }
    public string? ItemsJson { get; set; }    // JSON: List<NormalOrderItemSnapshot>

    // Side-effect tracking (compensation)
    public Guid? ReservationId { get; set; }
    public Guid? CouponClaimId { get; set; }

    // Payment
    public string? PaymentSessionId { get; set; }

    // Scheduled timeout tokens
    public Guid? StepTimeoutTokenId    { get; set; }   // per-step (30s)
    public Guid? PaymentExpiryTokenId  { get; set; }   // payment window (30min từ config)

    // Failure info
    public string? FailureStep   { get; set; }
    public string? FailureReason { get; set; }
}
```

---

### 6. PlaceOrderNormalSagaStateMachine (outline)

```csharp
public sealed class PlaceOrderNormalSagaStateMachine
    : SagaStateMachineBase<PlaceOrderNormalSagaState>
{
    // States
    public State PromotionRedeeming { get; private set; } = default!;
    public State InventoryReserving { get; private set; } = default!;
    public State CouponClaiming     { get; private set; } = default!;
    public State PaymentPending     { get; private set; } = default!;

    // Per-step timeout (30s) — tái sử dụng SagaStepTimeoutV1
    public Schedule<PlaceOrderNormalSagaState, SagaStepTimeoutV1> StepTimeout
        { get; private set; } = default!;

    // Payment expiry timeout — từ config, dùng PaymentExpiryTimeoutV1
    public Schedule<PlaceOrderNormalSagaState, PaymentExpiryTimeoutV1> PaymentExpiry
        { get; private set; } = default!;

    // Events: PlaceOrderRequestedV1, NormalOrderPromotionRedeemedV1,
    //   NormalOrderPromotionRedeemFailedV1, InventoryReservedV1, InventoryReserveFailedV1,
    //   CouponClaimedV1, CouponClaimFailedV1, PaymentSessionCreatedV1, PaymentCompletedV1

    public PlaceOrderNormalSagaStateMachine(
        IOptions<OrderPaymentOptions> paymentOptions,
        ILogger<PlaceOrderNormalSagaStateMachine> logger)
        : base(logger)
    {
        ConfigureSchedule(paymentOptions.Value);

        Initially(When(Requested)
            .Then(SnapshotRequest)
            .Schedule(StepTimeout, ...)
            .IfElse(ctx => ctx.Saga.CouponCode != null,
                then => then.Publish(BuildRedeemPromotionEvent).TransitionTo(PromotionRedeeming),
                else_ => else_.Publish(BuildReserveInventoryEvent).TransitionTo(InventoryReserving)));

        During(PromotionRedeeming, /* ... */ );
        During(InventoryReserving, /* ... */ );
        During(CouponClaiming,     /* ... */ );

        During(PaymentPending,
            When(PaymentSessionCreated)
                .ThenAsync(ctx => UpdateOrderPaymentSessionAsync(ctx))  // SetPaymentSession on Order
                .Ignore(),  // stay in PaymentPending

            When(PaymentCompleted)
                .Unschedule(PaymentExpiry)
                .ThenAsync(ctx => MarkOrderPaidAsync(ctx))
                .Finalize(),

            When(PaymentExpiry.Received)
                .Then(ctx => {
                    ctx.Saga.FailureStep = "PaymentExpiry";
                    ctx.Saga.FailureReason = "Payment window expired.";
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
                .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired", ctx.CancellationToken))
                .TransitionTo(Faulted));

        WhenEnter(PaymentPending, binder => binder
            .ThenAsync(ConfirmOrderAsync)              // CONFIRMED + SetConfirmedWithReservation
            .Schedule(PaymentExpiry,                   // start 30-min countdown
                ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
            .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(new
            {
                CorrelationId = ctx.Saga.OrderId.ToString("D"),
                ctx.Saga.OrderId,
                IdempotencyKey = $"{ctx.Saga.IdempotencyKey}:pay",
                Amount = ctx.Saga.Subtotal - ctx.Saga.PromotionDiscount
                         - ctx.Saga.CouponDiscount + ctx.Saga.ShippingFee,
                Currency = "VND"
            })));

        SetCompletedWhenFinalized();
        RegisterStateLogging();
    }

    private void ConfigureSchedule(OrderPaymentOptions opts)
    {
        Schedule(() => StepTimeout, x => x.StepTimeoutTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromSeconds(30);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromMinutes(opts.NormalOrderExpiryMinutes);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });
    }
}
```

---

### 7. PlaceSalesOrderSaga — payment changes

**`PlaceSalesOrderSagaState.cs`** — thêm field:
```csharp
public Guid? PaymentExpiryTokenId { get; set; }
public string? PaymentSessionId { get; set; }
```

**`PlaceSalesOrderSagaStateMachine.cs`** — thêm `PaymentPending` state, sửa `Confirming`:

```csharp
// Thêm state
public State PaymentPending { get; private set; } = default!;

// Thêm schedule
public Schedule<PlaceSalesOrderSagaState, PaymentExpiryTimeoutV1> PaymentExpiry
    { get; private set; } = default!;

// Thêm events
public Event<PaymentSessionCreatedV1> PaymentSessionCreated { get; private set; } = default!;
public Event<PaymentCompletedV1> PaymentCompleted { get; private set; } = default!;

// Trong constructor — đổi Confirming:
// TRƯỚC:
WhenEnter(Confirming, binder => binder.Finalize());

// SAU:
WhenEnter(Confirming, binder => binder
    .ThenAsync(ConfirmOrderAsSalesAsync)           // SetConfirmedAsSalesOrder
    .Schedule(PaymentExpiry,
        ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
    .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(BuildPaymentSession(ctx.Saga)))
    .TransitionTo(PaymentPending));

During(PaymentPending,
    When(PaymentSessionCreated)
        .ThenAsync(ctx => UpdateOrderPaymentSessionAsync(ctx))
        .Ignore(),

    When(PaymentCompleted)
        .Unschedule(PaymentExpiry)
        .ThenAsync(ctx => MarkOrderPaidAsync(ctx))
        .Finalize(),

    When(PaymentExpiry.Received)
        .Then(ctx => {
            ctx.Saga.FailureStep = "PaymentExpiry";
            ctx.Saga.FailureReason = "Payment window expired.";
        })
        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(...))
        .If(ctx => ctx.Saga.CouponClaimId.HasValue,
            b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(...)))
        .If(ctx => ctx.Saga.QuotaReserved,
            b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(...)))
        .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired", ...))
        .TransitionTo(Faulted));

// ConfigureSchedule — đọc từ config:
Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
{
    cfg.Delay = TimeSpan.FromMinutes(opts.SalesOrderExpiryMinutes);  // 15 phút
    cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
});
```

---

### 8. API — 202 Accepted

```csharp
// OrderApis.cs — PlaceOrderV1
var result = await sender.Send(body, ct);
if (result.IsFailure) return HandleFailure(result);
return Results.Accepted(
    uri:   $"/api/v1/orders/{result.Value}",
    value: new { orderId = result.Value, status = "Pending" });
```

---

### 9. EF migration

Thêm sau khi tất cả schema changes xong (order entity fields mới, saga state tables):

```bash
# Từ Order.Persistence directory
dotnet ef migrations add AddPaymentSessionFieldsAndNormalSagaState
```

Changes:
- `orders` table: thêm `payment_url`, `qr_code_url`, `PaymentStatus = AWAITING_PAYMENT`
- `place_order_normal_saga_states` table: **mới** (EF saga repository)
- `place_sales_order_saga_states` table: thêm `payment_expiry_token_id`, `payment_session_id`

---

## Implementation rules

1. **Command handler** không còn inject HTTP clients — chỉ `IOrderRepository`, `IOutboxWriter`, validators, `IUserContext`.
2. **Config**: `NormalOrderExpiryMinutes` và `SalesOrderExpiryMinutes` bắt buộc trong `appsettings.json`; `.ValidateOnStart()` fail fast nếu thiếu.
3. **PaymentExpiry timer** schedule ngay khi `WhenEnter(PaymentPending)` — không delay sau `PaymentSessionCreatedV1`.
4. **Compensation order** khi payment expired: release inventory → release coupon → release quota (sales) → cancel order. Thứ tự ngược lại acquire.
5. **`CorrelationId` = `OrderId`** cho cả normal và sales saga — MassTransit router phân biệt saga type bằng state machine class, không conflict.
6. **`PaymentSessionCreatedV1`** không đổi state — saga ở lại `PaymentPending`, chỉ update `PaymentUrl`/`QrCodeUrl` trên Order entity.
7. **MassTransit scheduler**: Quartz hoặc InMemory scheduler phải được register (hiện tại PlaceSalesOrder đã dùng) — đảm bảo `PaymentExpiry` schedule hoạt động.

---

## Acceptance criteria

### Normal order — async flow
- [ ] `POST /orders` trả `202 Accepted { orderId, status:"Pending" }` trong < 50ms.
- [ ] `GET /orders/{id}` ngay sau: status `PENDING`; sau saga xong (no coupon, no fail): `CONFIRMED`.

### Payment flow (cả normal và sales)
- [ ] Sau khi `CONFIRMED`, `GET /orders/{id}` trả `paymentUrl` và `qrCodeUrl` (không null).
- [ ] `PaymentStatus = AWAITING_PAYMENT` khi chờ; `PAID` sau `PaymentCompletedV1`.
- [ ] Simulate `PaymentCompletedV1` → order `PaymentStatus = PAID`, saga finalized.

### Expiry
- [ ] Normal: config `NormalOrderExpiryMinutes = 1` (test only) → sau 1 phút không có payment → order `CANCELLED`, reason = "Payment expired", inventory released.
- [ ] Sales: config `SalesOrderExpiryMinutes = 1` → tương tự + sale quota released.
- [ ] Khi `PaymentCompletedV1` đến trước timeout → timer cancelled (Unschedule), order PAID, không có false compensation.

### Compensation correctness
- [ ] Promotion fail (normal) → order `CANCELLED`, không có reservation.
- [ ] Inventory fail (normal, promotion đã redeem) → promotion quota restored, order `CANCELLED`.
- [ ] Payment expired (normal, inventory + coupon đã acquired) → cả hai released, order `CANCELLED`.
- [ ] Payment expired (sales) → inventory + coupon + quota released, order `CANCELLED`.

### Config
- [ ] Xoá `Order:Payment` section khỏi `appsettings.json` → app fail on startup với rõ ràng validation error.
- [ ] Đổi `NormalOrderExpiryMinutes = 5` → timer fires sau ~5 phút (không phải 30).

---

## Testing notes

- **Unit test `PlaceOrderCommandHandler`**: verify không còn HTTP calls, `PlaceOrderRequestedV1` published đúng fields.
- **Unit test sagas**: MassTransit `InMemoryTestHarness` — test từng state transition, verify messages published.
- **Payment expiry test**: dùng `IMessageScheduler` mock hoặc `InMemoryScheduler` với `AdvanceTime()`.
- **Integration test**: AppHost local, trace qua Aspire Dashboard, verify state transitions.

---

## Related changes

| File / Area | Service | Ghi chú |
|---|---|---|
| Payment service — consumer `CreatePaymentSessionV1` | Payment | Tạo Stripe/VNPay session → publish `PaymentSessionCreatedV1`; nhận webhook → publish `PaymentCompletedV1` |
| Inventory service — `InventoryReleaseRequestedV1` consumer | Inventory | Đã có từ PlaceSalesOrder saga — reuse |
| Promotion service — `CouponReleaseRequestedV1` consumer | Promotion | Đã có từ PlaceSalesOrder saga — reuse |
| Promotion service — `NormalOrderPromotionConsumer` | Promotion | **Mới**: xử lý `RedeemPromotionForNormalOrderRequestedV1` (không có `CampaignId`) |

---

## Reference

- PlaceSalesOrderSagaStateMachine (existing pattern): [PlaceSalesOrderSagaStateMachine.cs](../../../src/Services/Order/UrbanX.Order.Application/Sagas/PlaceSalesOrderSagaStateMachine.cs)
- PlaceSalesOrderSagaState: [PlaceSalesOrderSagaState.cs](../../../src/Services/Order/UrbanX.Order.Application/Sagas/PlaceSalesOrderSagaState.cs)
- PlaceOrder handler (hiện tại): [PlaceOrderCommandHandler.cs](../../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderCommandHandler.cs)
- PlaceOrderSaga contracts (reuse): [Shared.Contract/Messaging/PlaceOrderSaga/](../../../src/Shared/Shared.Contract/Messaging/PlaceOrderSaga/)
- Order domain model: [Order.cs](../../../src/Services/Order/UrbanX.Order.Domain/Models/Order.cs)
- OrderApis endpoint: [OrderApis.cs](../../../src/Services/Order/UrbanX.Order.API/Apis/OrderApis.cs)
- Order appsettings: [appsettings.json](../../../src/Services/Order/UrbanX.Order.API/appsettings.json)
