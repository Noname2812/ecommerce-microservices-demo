# TASK-07 — Refactor Place Order Normal Saga

**Team:** Order · **Effort:** L (2.5d) · **Depends:** TASK-01, TASK-02, TASK-03, TASK-05, TASK-09
**Branch:** `feature/order-refactor/TASK-07-saga-normal`

## Mục đích

Refactor `PlaceOrderNormalSagaStateMachine` để:
1. Thêm states `Validating`, `OrderPersisting`, `PaymentSessionCreating`
2. Drop state `PromotionRedeeming` (Normal flow không cần redeem promotion riêng)
3. Validate via `ICatalogServiceClient` HTTP (TASK-05)
4. Saga tự save Order với `Status=PROCESSING`
5. Khi PaymentSessionCreated → `MarkReadyForPayment` (PROCESSING → PENDING_PAYMENT)
6. Khi PaymentCompleted → publish `ConfirmInventoryRequestedV1` (TASK-09) + `MarkPaid` (CONFIRMED) + publish `OrderConfirmedV1`
7. Khi PaymentExpiry → release inventory + coupon + `Cancel` + publish `OrderCancelledV1` + release pending slot
8. Idempotency: deterministic MessageId cho saga publish (dedup qua retry)

## Files

### Rewrite `Order.Application/Sagas/PlaceOrderNormalSagaStateMachine.cs`

**States:**
```csharp
public State Validating              { get; private set; } = default!;
public State OrderPersisting         { get; private set; } = default!;
public State InventoryReserving      { get; private set; } = default!;
public State CouponClaiming          { get; private set; } = default!;
public State PaymentSessionCreating  { get; private set; } = default!;
public State PaymentPending          { get; private set; } = default!;
// DROP: PromotionRedeeming
```

**Events:** giữ events hiện có + KHÔNG còn `NormalOrderPromotionRedeemedV1/Failed`. Sau task này, có thể delete 2 events đó trong `Shared.Contract` (coordinate Phase 8).

**Flow:**
```csharp
Initially(
    When(Requested)
        .Then(SnapshotRequest)
        .ThenAsync(ctx => ValidateThroughCatalogAsync(ctx))
        .IfElse(ctx => ctx.Saga.ValidationError != null,
            fail => fail
                .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga))
                .ThenAsync(ctx => PublishOrderCancelledAsync(ctx, ctx.Saga.ValidationError!))
                .TransitionTo(Faulted),
            ok => ok
                .ThenAsync(ctx => CreateOrderProcessingAsync(ctx))
                .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(BuildInventoryRequest(ctx.Saga)))
                .TransitionTo(InventoryReserving)));

During(InventoryReserving,
    When(InventoryReserved)
        .Then(ctx => { ctx.Saga.ReservationId = ctx.Message.ReservationId; StampInstance(ctx.Saga); })
        .Unschedule(StepTimeout)
        .IfElse(ctx => ctx.Saga.CouponCode != null,
            hasCoupon => hasCoupon
                .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<ClaimCouponRequestedV1>(BuildCouponRequest(ctx.Saga)))
                .TransitionTo(CouponClaiming),
            noCoupon => noCoupon
                .TransitionTo(PaymentSessionCreating)),

    When(InventoryReserveFailed)
        .Then(ctx => { ctx.Saga.FailureStep = "InventoryReserve"; ctx.Saga.FailureReason = ctx.Message.ErrorMessage; StampInstance(ctx.Saga); })
        .Unschedule(StepTimeout)
        .TransitionTo(Compensating),

    When(StepTimeout.Received)
        .Then(ctx => { ctx.Saga.FailureStep = "InventoryTimeout"; ctx.Saga.FailureReason = "Inventory timeout"; StampInstance(ctx.Saga); })
        .TransitionTo(Compensating));

During(CouponClaiming,
    When(CouponClaimed)
        .Then(ctx => { ctx.Saga.CouponClaimId = ctx.Message.ClaimId; ctx.Saga.CouponDiscount = ctx.Message.DiscountAmount; StampInstance(ctx.Saga); })
        .Unschedule(StepTimeout)
        .TransitionTo(PaymentSessionCreating),

    When(CouponClaimFailed)
        .Then(ctx => { ctx.Saga.FailureStep = "CouponClaim"; ctx.Saga.FailureReason = ctx.Message.ErrorMessage; StampInstance(ctx.Saga); })
        .Unschedule(StepTimeout)
        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
        .TransitionTo(Compensating),

    When(StepTimeout.Received)
        .Then(ctx => { ctx.Saga.FailureStep = "CouponTimeout"; ctx.Saga.FailureReason = "Coupon timeout"; StampInstance(ctx.Saga); })
        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
        .TransitionTo(Compensating));

WhenEnter(PaymentSessionCreating, b => b
    .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
    .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(BuildPaymentSessionRequest(ctx.Saga))));

During(PaymentSessionCreating,
    When(PaymentSessionCreated)
        .Unschedule(StepTimeout)
        .ThenAsync(ctx => MarkReadyForPaymentAsync(ctx))
        .Schedule(PaymentExpiry, ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
        .TransitionTo(PaymentPending),

    When(StepTimeout.Received)
        .Then(ctx => { ctx.Saga.FailureStep = "PaymentSessionTimeout"; ctx.Saga.FailureReason = "Payment session create timeout"; StampInstance(ctx.Saga); })
        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
        .If(ctx => ctx.Saga.CouponClaimId.HasValue,
            b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
        .TransitionTo(Compensating));

During(PaymentPending,
    When(PaymentCompleted)
        .Unschedule(PaymentExpiry)
        .PublishAsync(ctx => ctx.Init<ConfirmInventoryRequestedV1>(BuildConfirmInventoryRequest(ctx.Saga)))
        .ThenAsync(ctx => MarkOrderPaidAsync(ctx))
        .ThenAsync(ctx => PublishOrderConfirmedAsync(ctx))
        .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga))
        .Finalize(),

    When(PaymentExpiry.Received)
        .Then(ctx => { ctx.Saga.FailureStep = "PaymentExpiry"; ctx.Saga.FailureReason = "Payment expired"; StampInstance(ctx.Saga); })
        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
        .If(ctx => ctx.Saga.CouponClaimId.HasValue,
            b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
        .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired"))
        .ThenAsync(ctx => PublishOrderCancelledAsync(ctx, "Payment expired"))
        .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga))
        .TransitionTo(Faulted));

WhenEnter(Compensating, b => b
    .If(ctx => ctx.Saga.ReservationId.HasValue && ctx.Saga.FailureStep != "CouponClaim"
                                              && ctx.Saga.FailureStep != "CouponTimeout"
                                              && ctx.Saga.FailureStep != "PaymentSessionTimeout",
        x => x.PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga))))
    .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, ctx.Saga.FailureReason ?? "Order failed"))
    .ThenAsync(ctx => PublishOrderCancelledAsync(ctx, ctx.Saga.FailureReason ?? "Order failed"))
    .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga))
    .TransitionTo(Faulted));
```

**Saga methods (dùng `_scopeFactory`):**

```csharp
private async Task ValidateThroughCatalogAsync(BehaviorContext<PlaceOrderNormalSagaState, PlaceOrderRequestedV1> ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var catalog = scope.ServiceProvider.GetRequiredService<ICatalogServiceClient>();

    var variantIds = ctx.Message.Items.Select(i => i.VariantId).Distinct().ToArray();
    var result = await catalog.GetVariantsAsync(variantIds, CancellationToken.None);

    if (result.IsFailure)
    {
        ctx.Saga.ValidationError = result.Error.Code == "Order.CatalogUnavailable"
            ? "CATALOG_UNAVAILABLE"
            : "VARIANT_VALIDATION_FAILED";
        StampInstance(ctx.Saga);
        return;
    }

    var validation = ValidateBusinessRules(result.Value, ctx.Message.Items, ctx.Message.PricingSnapshot);
    if (validation.IsFailure)
    {
        ctx.Saga.ValidationError = validation.Error.Code;
        StampInstance(ctx.Saga);
        return;
    }

    // Save variant info vào saga state để CreateOrder dùng (avoid second HTTP call)
    ctx.Saga.VariantsJson = JsonSerializer.Serialize(result.Value);
    StampInstance(ctx.Saga);
}

private async Task CreateOrderProcessingAsync(BehaviorContext<PlaceOrderNormalSagaState, PlaceOrderRequestedV1> ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    var variants = JsonSerializer.Deserialize<List<CatalogVariantInfo>>(ctx.Saga.VariantsJson!)!
        .ToDictionary(v => v.VariantId);

    await uow.ExecuteInTransactionAsync(async () =>
    {
        var order = OrderFactory.BuildFromSaga(ctx.Saga, variants, ctx.Saga.OrderId);
        repo.Add(order);
    });
}

private async Task MarkReadyForPaymentAsync(BehaviorContext<PlaceOrderNormalSagaState, PaymentSessionCreatedV1> ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    await uow.ExecuteInTransactionAsync(async () =>
    {
        var order = await repo.GetByIdAsync(ctx.Saga.OrderId);
        if (order is null) return;
        var userId = Guid.Parse(ctx.Saga.UserId);
        order.MarkReadyForPayment(
            ctx.Saga.ReservationId!.Value, ctx.Saga.CouponClaimId,
            ctx.Message.PaymentUrl, ctx.Message.QrCodeUrl,
            userId, "");
    });

    ctx.Saga.PaymentSessionId = ctx.Message.PaymentSessionId;
    ctx.Saga.PaymentUrl        = ctx.Message.PaymentUrl;
    ctx.Saga.QrCodeUrl         = ctx.Message.QrCodeUrl;
    ctx.Saga.PaymentExpiresAt  = DateTimeOffset.UtcNow.AddMinutes(_paymentOptions.NormalOrderExpiryMinutes);
    StampInstance(ctx.Saga);
}

private async Task MarkOrderPaidAsync(BehaviorContext<PlaceOrderNormalSagaState, PaymentSessionCompletedV1> ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    await uow.ExecuteInTransactionAsync(async () =>
    {
        var order = await repo.GetByIdAsync(ctx.Saga.OrderId);
        if (order is null) return;
        var userId = Guid.Parse(ctx.Saga.UserId);
        order.MarkPaid(ctx.Message.PaymentSessionId, userId, "");
    });
}

private async Task CancelOrderAsync(PlaceOrderNormalSagaState saga, string reason) { /* gọi order.Cancel */ }

private async Task ReleasePendingSlotAsync(PlaceOrderNormalSagaState saga)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var slots = scope.ServiceProvider.GetRequiredService<IPendingOrderSlotService>();
    if (Guid.TryParse(saga.UserId, out var uid))
        await slots.ReleaseAsync(uid, CancellationToken.None);
}

private async Task PublishOrderConfirmedAsync(BehaviorContext ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
    var repo      = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var order     = await repo.GetByIdAsync(ctx.Saga.OrderId);
    if (order is null) return;

    await publisher.Publish(new OrderConfirmedV1
    {
        OrderId       = order.Id,
        OrderNumber   = order.OrderNumber,
        UserId        = order.UserId,
        CustomerEmail = order.CustomerEmail,
        FinalAmount   = order.FinalAmount,
        ConfirmedAt   = DateTimeOffset.UtcNow,
        Items         = order.Items.Select(i =>
            new OrderItemSummary(i.ProductId, i.VariantId, i.VariantSku, i.Quantity, i.UnitPrice)).ToList()
    }, msgCtx =>
    {
        msgCtx.MessageId = DeterministicGuid($"order-confirmed:{order.Id}");
    });
}

private async Task PublishOrderCancelledAsync(BehaviorContext ctx, string reason)
{
    // Similar pattern, MessageId = DeterministicGuid($"order-cancelled:{ctx.Saga.OrderId}")
}

private static Guid DeterministicGuid(string input)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
    return new Guid(bytes.Take(16).ToArray());
}
```

### Rewrite `PlaceOrderNormalSagaState.cs` — thêm fields

```csharp
public string? ShippingAddressJson { get; set; }
public decimal ShippingFee         { get; set; }
public string  PricingSnapshot     { get; set; } = "{}";
public string  CustomerEmail       { get; set; } = "";
public string  CustomerName        { get; set; } = "";
public string? CustomerPhone       { get; set; }
public string? CustomerNote        { get; set; }
public string? VariantsJson        { get; set; }    // CatalogVariantInfo[] cached after validate
public string? ValidationError     { get; set; }
public string? PaymentUrl          { get; set; }
public string? QrCodeUrl           { get; set; }
public DateTimeOffset? PaymentExpiresAt { get; set; }
```

### Update EF Configuration

**`Order.Persistence/Configurations/PlaceOrderNormalSagaStateConfiguration.cs`** — thêm column config cho fields mới. Migration auto-generate ở TASK-12.

### Modify `OrderFactory.cs`
- Signature mới: `BuildFromSaga(PlaceOrderNormalSagaState saga, IDictionary<Guid, CatalogVariantInfo> variants, Guid orderId)`
- Build `Order.Create(orderId, orderNumber, userId, customerEmail, ..., items, OrderType.Normal)` với items được enrich từ `variants` dict

## Acceptance Criteria

- [ ] Build OK
- [ ] Unit test saga (dùng MassTransit TestHarness):
  - Happy: Requested → Validating → OrderPersisting → InventoryReserving → PaymentSessionCreating → PaymentPending → PaymentCompleted → Final
  - Validation fail → Faulted + OrderCancelledV1 published + slot release
  - Inventory fail → Compensating → Faulted
  - Payment timeout → release inv + coupon + Cancel + OrderCancelledV1 + slot release
- [ ] Integration test với Aspire local:
  - Submit PlaceOrder → poll ticket → eventually PENDING_PAYMENT với paymentUrl
  - Mock PaymentSessionCompleted → eventually CONFIRMED
  - Verify Inventory `confirm_at` set, QuantityOnHand giảm

## Notes

- **Idempotency**: domain methods đã có guards (TASK-02). Saga publish dùng `DeterministicGuid(MessageId)` để dedupe.
- **Deferred Catalog deletion**: 2 events `NormalOrderPromotionRedeemedV1/Failed` không còn dùng — confirm với Promotion team, có thể delete trong TASK-08 hoặc cleanup riêng.
- Scope factory pattern hiện có — vẫn dùng vì saga state machine là singleton, scoped service phải resolve qua scope.

## DoD

- [ ] All tests pass
- [ ] PR merge
- [ ] Unblock TASK-12, 13
