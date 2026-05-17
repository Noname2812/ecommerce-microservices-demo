# TASK-08 — Refactor Place Sales Order Flow (Flash Sale + Coupon)

**Team:** Order + Promotion · **Effort:** L (3d) · **Depends:** TASK-01, TASK-02, TASK-03, TASK-04, TASK-05, TASK-09
**Branch:** `feature/order-refactor/TASK-08-saga-sales`

## Mục đích

Refactor Sales flow theo design chi tiết (khác Normal):

```
Order Handler (sync < 20ms)
 [1] FluentValidation                       → input format
 [2] Pending limit check (Redis)            → max 3 pending/user (Sales được lỏng hơn Normal=1)
 [3] Flash sale gate (Redis Lua atomic)     → DECRBY stock — reject 409 nếu sold out
 → Publish PlaceSalesOrderRequestedV1
 → Return 202 { ticketId }

SAGA Orchestrator
 [1] Validate product/seller/price          → ICatalogServiceClient + server-side pricing recalc (lệch >1% → reject)
 [2] Validate flash sale còn hiệu lực       → ISaleEligibilityService (thời gian, quota, user-already-bought)
 [3] Validate & lock coupon (soft claim)    → ICouponLockService Lua atomic
 [4] Save Order { status = PENDING_PAYMENT } + snapshot pricing
 [5] Reserve inventory (soft lock)
 [6] Create payment session
 [7] Schedule 15-min timeout

Payment outcome
 ✅ PaymentCompleted
    [1] Hard deduct inventory
    [2] Confirm coupon use (hard deduct permanent)
    [3] Order = CONFIRMED
    [4] Publish OrderConfirmedV1
 ❌ PaymentTimeoutExpired
    [1] Release inventory soft lock
    [2] Release coupon lock
    [3] Restore Redis flash sale stock (INCRBY)
    [4] Order = CANCELLED
    [5] Publish OrderCancelledV1
```

## Nguyên tắc

### Server-side pricing — không tin client
```
originalPrice   = catalog.price × quantity        (server tính)
saleDiscount    = flash sale rule (%, fixed)      (server tính)
couponDiscount  = coupon rule (%, fixed)          (server tính)
shippingFee     = address + seller config         (server tính)
─────────────────────────────────────────────────
finalTotal      = originalPrice - saleDiscount - couponDiscount + shippingFee
```

Nếu `Math.Abs(expectedTotal - finalTotal) / finalTotal > 0.01` → reject với `OrderErrors.PriceMismatch` (409) + trả breakdown mới nhất.

### Flash sale stock — 3 thời điểm
| Thời điểm | Action |
|---|---|
| Handler [3] | Lua `DECRBY flashsale:{saleId}:stock` — atomic, reject `409 Sold Out` nếu hết |
| Saga [2] | Validate sale còn hiệu lực (time window, quota meta, user-already-bought set) |
| Timeout / fail | `INCRBY flashsale:{saleId}:stock` — trả lại |

### Coupon — 3 thời điểm
| Thời điểm | Action |
|---|---|
| Saga [3] | Lua atomic: check `coupon:{code}:eligible-users` SISMEMBER user → DECR `coupon:{code}:remaining` → SADD `coupon:{code}:locked-users` user → expire 16min |
| Payment success | Confirm: SREM `locked-users` user → SADD `coupon:{code}:used-users` user (permanent) |
| Timeout / cancel | Release: INCR `coupon:{code}:remaining` → SREM `locked-users` user |

## Files

### Handler — Files MODIFY

**`Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs`** (rewrite):

```csharp
public sealed class PlaceSalesOrderCommandHandler(
    IPublishEndpoint publishEndpoint,
    IPendingOrderSlotService pendingSlots,
    IFlashSaleStockService flashSaleStock,
    IUserContext userContext)
    : ICommandHandler<PlaceSalesOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceSalesOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        // [2] Pending limit — sales dùng MaxSalesPendingPerUser (default 3)
        var slot = await pendingSlots.TryAcquireAsync(userId, OrderType.Sales, ct);
        if (slot.IsFailure)
            return Result.Failure<Guid>(slot.Error);

        // [3] Flash sale gate — atomic DECRBY Redis
        var totalQty = cmd.Items.Sum(i => i.Quantity);
        var stockResult = await flashSaleStock.TryReserveAsync(cmd.CampaignId, totalQty, ct);
        if (stockResult.IsFailure)
        {
            await pendingSlots.ReleaseAsync(userId, ct);   // rollback slot
            return Result.Failure<Guid>(stockResult.Error);
        }

        var ticketId = Guid.NewGuid();
        await publishEndpoint.Publish(new PlaceSalesOrderRequestedV1
        {
            OrderId         = ticketId,
            UserId          = userId.ToString("D"),
            CampaignId      = cmd.CampaignId,
            IdempotencyKey  = cmd.IdempotencyKey,
            CouponCode      = cmd.CouponCode,
            ExpectedTotal   = cmd.ExpectedTotal,           // saga sẽ verify 1% tolerance
            ShippingAddress = MapShipping(cmd.ShippingAddress),
            ShippingFee     = cmd.ShippingFee,
            Items           = cmd.Items.Select(...).ToList()
        }, ct);

        return Result.Success(ticketId);
    }
}
```

### Handler — NEW infrastructure services

**`Order.Application/Services/IFlashSaleStockService.cs`:**
```csharp
public interface IFlashSaleStockService
{
    Task<Result> TryReserveAsync(Guid saleId, int quantity, CancellationToken ct);
    Task RestoreAsync(Guid saleId, int quantity, CancellationToken ct);
}
```

**`Order.Infrastructure/Services/RedisFlashSaleStockService.cs`:**
- Inject `ICacheService` (Lua eval)
- Key: `{instanceName}:flashsale:{saleId}:stock`
- Reserve Lua:
  ```lua
  local stock = tonumber(redis.call('GET', KEYS[1]) or '0')
  if stock < tonumber(ARGV[1]) then return -1 end
  return redis.call('DECRBY', KEYS[1], ARGV[1])
  ```
- Return `< 0` → `Result.Failure(OrderErrors.FlashSaleSoldOut(saleId))`
- Restore: `redis.call('INCRBY', KEYS[1], ARGV[1])`

⚠ Promotion service phải `SET flashsale:{saleId}:stock = quotaTotal` khi campaign start. Coordinate với Promotion team.

**`Order.Application/Services/ICouponLockService.cs`:**
```csharp
public interface ICouponLockService
{
    Task<Result<CouponLockInfo>> TryLockAsync(string couponCode, Guid userId, CancellationToken ct);
    Task ReleaseAsync(string couponCode, Guid userId, CancellationToken ct);
    Task ConfirmUseAsync(string couponCode, Guid userId, CancellationToken ct);
}

public record CouponLockInfo(decimal DiscountAmount, string DiscountType);
```

**`Order.Infrastructure/Services/RedisCouponLockService.cs`:**

Reserve Lua (atomic):
```lua
-- KEYS[1]: coupon:{code}:eligible-users (set)
-- KEYS[2]: coupon:{code}:remaining (string int)
-- KEYS[3]: coupon:{code}:locked-users (set)
-- KEYS[4]: coupon:{code}:used-users (set)
-- KEYS[5]: coupon:{code}:meta-discount (string)
-- KEYS[6]: coupon:{code}:meta-discount-type (string)
-- ARGV[1]: userId
-- ARGV[2]: lock TTL seconds (16min = 960)

if redis.call('SISMEMBER', KEYS[4], ARGV[1]) == 1 then return -1 end   -- đã dùng
if redis.call('SISMEMBER', KEYS[1], ARGV[1]) == 0 then return -2 end   -- không eligible
if redis.call('SISMEMBER', KEYS[3], ARGV[1]) == 1 then return -3 end   -- đã lock

local remaining = tonumber(redis.call('GET', KEYS[2]) or '0')
if remaining <= 0 then return -4 end                                   -- hết

redis.call('DECR', KEYS[2])
redis.call('SADD', KEYS[3], ARGV[1])
redis.call('EXPIRE', KEYS[3], ARGV[2])

return tonumber(redis.call('GET', KEYS[5]) or '0')                     -- return discount amount
```

Release Lua:
```lua
if redis.call('SREM', KEYS[3], ARGV[1]) == 1 then
    redis.call('INCR', KEYS[2])
end
return 1
```

ConfirmUse Lua (permanent):
```lua
redis.call('SREM', KEYS[3], ARGV[1])                  -- remove from locked
redis.call('SADD', KEYS[4], ARGV[1])                  -- add to used (permanent)
return 1
```

⚠ Promotion service init keys khi coupon được create:
- `SET coupon:{code}:remaining = totalQuota`
- `SADD coupon:{code}:eligible-users user1 user2 ...` (hoặc dùng pattern match thay vì set)
- `SET coupon:{code}:meta-discount = 50000` (VND fixed)
- `SET coupon:{code}:meta-discount-type = "FIXED"` hoặc `"PERCENT"`

**`Order.Application/Services/ISaleEligibilityService.cs`:**
```csharp
public interface ISaleEligibilityService
{
    Task<Result<SaleEligibility>> ValidateAsync(
        Guid campaignId, Guid userId, IReadOnlyList<OrderItemSnapshot> items, CancellationToken ct);
}

public record SaleEligibility(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal SaleDiscountAmount,
    string DiscountType);
```

Implementation: HTTP call Promotion service `GET /api/v1/promotion/campaigns/{id}/validate?userId=...&items=...`. Trả về:
- `200` + eligibility info nếu valid
- `400` `SALE_EXPIRED` / `USER_ALREADY_BOUGHT` / `QUOTA_EXHAUSTED`
- `404` `CAMPAIGN_NOT_FOUND`

Add Polly resilience (giống ICatalogServiceClient — TASK-05).

### Errors

**`Order.Domain/Errors/OrderErrors.cs`** — thêm:
```csharp
public static Error FlashSaleSoldOut(Guid saleId) =>
    new("Order.FlashSaleSoldOut", $"Flash sale {saleId} is sold out");
public static readonly Error SaleExpired =
    new("Order.SaleExpired", "Flash sale has expired");
public static readonly Error UserAlreadyBoughtFromSale =
    new("Order.UserAlreadyBoughtFromSale", "User already bought from this sale");
public static readonly Error PriceMismatch =
    new("Order.PriceMismatch", "Server-calculated price differs from expected (>1%)");
public static readonly Error CouponNotEligible =
    new("Order.CouponNotEligible", "User is not eligible for this coupon");
public static readonly Error CouponAlreadyUsed =
    new("Order.CouponAlreadyUsed", "User has already used this coupon");
public static readonly Error CouponExhausted =
    new("Order.CouponExhausted", "Coupon has no remaining quota");
```

ApiEndpoint mapping:
- `Order.FlashSaleSoldOut`, `Order.SaleExpired`, `Order.PriceMismatch`, `Order.CouponAlreadyUsed` → 409 Conflict
- `Order.CouponNotEligible` → 403 Forbidden
- `Order.CouponExhausted` → 410 Gone

### Saga — Rewrite `PlaceSalesOrderSagaStateMachine.cs`

**States:**
```csharp
public State Validating              { get; private set; } = default!;   // [1] product/seller/price
public State SaleValidating          { get; private set; } = default!;   // [2] sale eligibility
public State CouponLocking           { get; private set; } = default!;   // [3] coupon lock
public State OrderPersisting         { get; private set; } = default!;   // [4] save Order
public State InventoryReserving      { get; private set; } = default!;   // [5] reserve
public State PaymentSessionCreating  { get; private set; } = default!;   // [6] payment session
public State PaymentPending          { get; private set; } = default!;   // [7] timeout 15 phút
```

**Flow (high-level):**

```csharp
Initially(
    When(Requested)
        .Then(SnapshotRequest)
        .ThenAsync(ValidateThroughCatalogAsync)               // [1]
        .IfElse(ctx => ctx.Saga.ValidationError != null,
            fail => fail.ThenAsync(CompensateValidationFailAsync).TransitionTo(Faulted),
            ok => ok.TransitionTo(SaleValidating)));

WhenEnter(SaleValidating, b => b.ThenAsync(ValidateSaleEligibilityAsync));   // [2]
During(SaleValidating,
    When(SaleValidated)
        .IfElse(ctx => ctx.Saga.CouponCode != null,
            hasCoupon => hasCoupon.TransitionTo(CouponLocking),
            noCoupon => noCoupon.TransitionTo(OrderPersisting)),
    When(SaleValidationFailed)
        .ThenAsync(CompensateSaleFailAsync)                   // restore flash sale stock
        .TransitionTo(Faulted));

WhenEnter(CouponLocking, b => b.ThenAsync(LockCouponAsync));   // [3]
During(CouponLocking,
    When(CouponLocked)
        .TransitionTo(OrderPersisting),
    When(CouponLockFailed)
        .ThenAsync(CompensateCouponFailAsync)                 // restore flash sale stock
        .TransitionTo(Faulted));

WhenEnter(OrderPersisting, b => b.ThenAsync(CreateSalesOrderAsync));   // [4]
During(OrderPersisting,
    When(OrderPersisted)
        .Publish(...)
        .TransitionTo(InventoryReserving),
    When(OrderPersistFailed)
        .ThenAsync(CompensateOrderPersistFailAsync)
        .TransitionTo(Faulted));

During(InventoryReserving,
    When(InventoryReserved) → ... → TransitionTo(PaymentSessionCreating),    // [5]
    When(InventoryReserveFailed) → CompensateInventoryFailAsync → Faulted);

WhenEnter(PaymentSessionCreating, b => b.Publish(CreatePaymentSessionV1));   // [6]
During(PaymentSessionCreating,
    When(PaymentSessionCreated)
        .ThenAsync(MarkSalesReadyForPaymentAsync)
        .Schedule(PaymentExpiry, ...)                                          // [7]
        .TransitionTo(PaymentPending));

During(PaymentPending,
    When(PaymentCompleted)
        .Publish(ConfirmInventoryRequestedV1)
        .ThenAsync(ConfirmCouponUseAsync)              // ✅ hard deduct coupon permanent
        .ThenAsync(MarkOrderPaidAsync)
        .ThenAsync(PublishOrderConfirmedAsync)
        .ThenAsync(ReleasePendingSlotAsync)
        .Finalize(),

    When(PaymentExpiry.Received)
        .Publish(InventoryReleaseRequestedV1)          // ❌ release inv
        .ThenAsync(ReleaseCouponLockAsync)             // release coupon
        .ThenAsync(RestoreFlashSaleStockAsync)         // INCRBY Redis
        .ThenAsync(CancelOrderAsync, "Payment expired")
        .ThenAsync(PublishOrderCancelledAsync)
        .ThenAsync(ReleasePendingSlotAsync)
        .TransitionTo(Faulted));
```

**Saga methods:**

```csharp
private async Task ValidateThroughCatalogAsync(BehaviorContext ctx)
{
    // Same as Normal saga TASK-07
    // Plus: tính lại finalTotal server-side, so với ExpectedTotal
    var calculatedTotal = CalculateFinalTotal(variants, items, ctx.Message.ShippingFee, saleDiscount=0, couponDiscount=0);
    if (Math.Abs(ctx.Message.ExpectedTotal - calculatedTotal) / calculatedTotal > 0.01m)
    {
        ctx.Saga.ValidationError = "PRICE_MISMATCH";
        StampInstance(ctx.Saga);
    }
}

private async Task ValidateSaleEligibilityAsync(BehaviorContext ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var saleService = scope.ServiceProvider.GetRequiredService<ISaleEligibilityService>();
    var items = JsonSerializer.Deserialize<List<OrderItemSnapshot>>(ctx.Saga.ItemsJson!)!;
    var result = await saleService.ValidateAsync(
        ctx.Saga.CampaignId, Guid.Parse(ctx.Saga.UserId), items, CancellationToken.None);

    if (result.IsFailure)
    {
        ctx.Saga.ValidationError = result.Error.Code;
        StampInstance(ctx.Saga);
        await ctx.Raise(SaleValidationFailed);
        return;
    }

    ctx.Saga.SaleDiscount        = result.Value.SaleDiscountAmount;
    ctx.Saga.SaleStartAt         = result.Value.StartAt;
    ctx.Saga.SaleEndAt           = result.Value.EndAt;
    StampInstance(ctx.Saga);
    await ctx.Raise(SaleValidated);
}

private async Task LockCouponAsync(BehaviorContext ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var coupon = scope.ServiceProvider.GetRequiredService<ICouponLockService>();
    var result = await coupon.TryLockAsync(
        ctx.Saga.CouponCode!, Guid.Parse(ctx.Saga.UserId), CancellationToken.None);

    if (result.IsFailure)
    {
        ctx.Saga.FailureReason = result.Error.Code;
        StampInstance(ctx.Saga);
        await ctx.Raise(CouponLockFailed);
        return;
    }

    ctx.Saga.CouponDiscount = result.Value.DiscountAmount;
    StampInstance(ctx.Saga);
    await ctx.Raise(CouponLocked);
}

private async Task CreateSalesOrderAsync(BehaviorContext ctx)
{
    // Compute snapshot pricing
    var pricing = new {
        OriginalPrice  = items.Sum(i => i.UnitPrice * i.Quantity),
        SaleDiscount   = ctx.Saga.SaleDiscount,
        CouponDiscount = ctx.Saga.CouponDiscount,
        ShippingFee    = ctx.Saga.ShippingFee,
        FinalTotal     = /* compute */
    };

    await using var scope = _scopeFactory.CreateAsyncScope();
    var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
    await uow.ExecuteInTransactionAsync(async () =>
    {
        var order = OrderFactory.BuildSalesFromSaga(ctx.Saga, variants, pricing, ctx.Saga.OrderId);
        repo.Add(order);
    });
    await ctx.Raise(OrderPersisted);
}

private async Task ConfirmCouponUseAsync(BehaviorContext ctx)
{
    if (string.IsNullOrEmpty(ctx.Saga.CouponCode)) return;
    await using var scope = _scopeFactory.CreateAsyncScope();
    var coupon = scope.ServiceProvider.GetRequiredService<ICouponLockService>();
    await coupon.ConfirmUseAsync(ctx.Saga.CouponCode, Guid.Parse(ctx.Saga.UserId), CancellationToken.None);
}

private async Task ReleaseCouponLockAsync(BehaviorContext ctx)
{
    if (string.IsNullOrEmpty(ctx.Saga.CouponCode)) return;
    await using var scope = _scopeFactory.CreateAsyncScope();
    var coupon = scope.ServiceProvider.GetRequiredService<ICouponLockService>();
    await coupon.ReleaseAsync(ctx.Saga.CouponCode, Guid.Parse(ctx.Saga.UserId), CancellationToken.None);
}

private async Task RestoreFlashSaleStockAsync(BehaviorContext ctx)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var stock = scope.ServiceProvider.GetRequiredService<IFlashSaleStockService>();
    var totalQty = JsonSerializer.Deserialize<List<OrderItemSnapshot>>(ctx.Saga.ItemsJson!)!.Sum(i => i.Quantity);
    await stock.RestoreAsync(ctx.Saga.CampaignId, totalQty, CancellationToken.None);
}

// Compensation helpers per step
private async Task CompensateValidationFailAsync(BehaviorContext ctx)
{
    await RestoreFlashSaleStockAsync(ctx);
    await PublishOrderCancelledAsync(ctx, ctx.Saga.ValidationError ?? "Validation failed");
    await ReleasePendingSlotAsync(ctx.Saga);
}

private async Task CompensateSaleFailAsync(BehaviorContext ctx) {
    await RestoreFlashSaleStockAsync(ctx);
    await PublishOrderCancelledAsync(ctx, ctx.Saga.ValidationError ?? "Sale validation failed");
    await ReleasePendingSlotAsync(ctx.Saga);
}

private async Task CompensateCouponFailAsync(BehaviorContext ctx) {
    await RestoreFlashSaleStockAsync(ctx);
    await PublishOrderCancelledAsync(ctx, ctx.Saga.FailureReason ?? "Coupon lock failed");
    await ReleasePendingSlotAsync(ctx.Saga);
}

private async Task CompensateOrderPersistFailAsync(BehaviorContext ctx) {
    await ReleaseCouponLockAsync(ctx);
    await RestoreFlashSaleStockAsync(ctx);
    await PublishOrderCancelledAsync(ctx, "Save order failed");
    await ReleasePendingSlotAsync(ctx.Saga);
}

private async Task CompensateInventoryFailAsync(BehaviorContext ctx) {
    await ReleaseCouponLockAsync(ctx);
    await RestoreFlashSaleStockAsync(ctx);
    await CancelOrderAsync(ctx.Saga, ctx.Saga.FailureReason ?? "Inventory reserve failed");
    await PublishOrderCancelledAsync(ctx, ctx.Saga.FailureReason ?? "Inventory reserve failed");
    await ReleasePendingSlotAsync(ctx.Saga);
}

// Plus: MarkOrderPaidAsync, MarkSalesReadyForPaymentAsync, CancelOrderAsync, etc.
// Plus: PublishOrderConfirmedAsync, PublishOrderCancelledAsync — same pattern Normal saga
```

### Saga state — Rewrite `PlaceSalesOrderSagaState.cs`

```csharp
public sealed class PlaceSalesOrderSagaState : SagaStateBase
{
    public Guid OrderId            { get; set; }
    public string UserId           { get; set; } = "";
    public Guid CampaignId         { get; set; }
    public string IdempotencyKey   { get; set; } = "";
    public string? CouponCode      { get; set; }
    public string ItemsJson        { get; set; } = "";
    public string? VariantsJson    { get; set; }
    public decimal ExpectedTotal   { get; set; }
    public decimal ShippingFee     { get; set; }
    public decimal SaleDiscount    { get; set; }
    public decimal CouponDiscount  { get; set; }
    public decimal FinalTotal      { get; set; }
    public DateTimeOffset? SaleStartAt { get; set; }
    public DateTimeOffset? SaleEndAt   { get; set; }
    public Guid? ReservationId     { get; set; }
    public Guid? PaymentSessionId  { get; set; }
    public string? PaymentUrl      { get; set; }
    public string? QrCodeUrl       { get; set; }
    public DateTimeOffset? PaymentExpiresAt { get; set; }
    public string? ShippingAddressJson { get; set; }
    public string CustomerEmail    { get; set; } = "";
    public string CustomerName     { get; set; } = "";
    public string? CustomerPhone   { get; set; }
    public string? CustomerNote    { get; set; }
    public string? ValidationError { get; set; }
    public string? FailureStep     { get; set; }
    public string? FailureReason   { get; set; }
    public Guid? StepTimeoutTokenId   { get; set; }
    public Guid? PaymentExpiryTokenId { get; set; }
}
```

### OrderFactory — thêm method

**`Order.Application/Usecases/V1/Command/Common/OrderFactory.cs`:**

```csharp
public static Order BuildSalesFromSaga(
    PlaceSalesOrderSagaState saga,
    IDictionary<Guid, CatalogVariantInfo> variants,
    SalesPricingSnapshot pricing,
    Guid orderId)
{
    var items = JsonSerializer.Deserialize<List<OrderItemSnapshot>>(saga.ItemsJson)!
        .Select(i => /* enrich từ variants */)
        .ToList();

    return Order.Create(
        orderId:        orderId,
        orderNumber:    OrderNumberGenerator.Generate("SAL"),
        userId:         Guid.Parse(saga.UserId),
        customerEmail:  saga.CustomerEmail,
        customerName:   saga.CustomerName,
        customerPhone:  saga.CustomerPhone,
        shippingAddress: DeserializeShipping(saga.ShippingAddressJson!),
        shippingFee:    pricing.ShippingFee,
        couponCode:     saga.CouponCode,
        couponDiscount: pricing.CouponDiscount,
        // NEW: saleDiscount + originalPrice — cần thêm vào Order.Create signature (TASK-02 update)
        saleDiscount:   pricing.SaleDiscount,
        originalPrice:  pricing.OriginalPrice,
        customerNote:   saga.CustomerNote,
        idempotencyKey: saga.IdempotencyKey,
        items:          items,
        orderType:      OrderType.Sales,
        campaignId:     saga.CampaignId);
}

public record SalesPricingSnapshot(
    decimal OriginalPrice,
    decimal SaleDiscount,
    decimal CouponDiscount,
    decimal ShippingFee,
    decimal FinalTotal);
```

⚠ `Order.Create` cần thêm 2 params `saleDiscount`, `originalPrice` — coordinate với TASK-02 (Order domain refactor) trước khi merge.

### DI Registration

**`Order.Application/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`:**
Bind `PlaceOrderOptions` đã có; verify fields mới:
```csharp
public sealed class PlaceOrderOptions
{
    public const string SectionName = "PlaceOrder";

    public int MaxNormalPendingPerUser { get; init; } = 1;
    public int MaxSalesPendingPerUser  { get; init; } = 3;
    public int PendingSlotTtlMinutes   { get; init; } = 30;
    public int CouponLockTtlSeconds    { get; init; } = 960;  // 16 phút (saga timeout 15p + buffer)
    public decimal PriceMismatchTolerance { get; init; } = 0.01m;  // 1%
}
```

**`Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`:**
```csharp
services.AddSingleton<IFlashSaleStockService, RedisFlashSaleStockService>();
services.AddSingleton<ICouponLockService, RedisCouponLockService>();

services.AddHttpClient<ISaleEligibilityService, PromotionSaleEligibilityClient>(...)
    .AddStandardResilienceHandler(/* same as Catalog client TASK-05 */);
```

### Promotion service — coordinate

Promotion team cần:

1. **Khi campaign create:** seed Redis `flashsale:{saleId}:stock = quotaTotal`
2. **Khi campaign expire:** cleanup Redis keys (TTL hoặc job)
3. **Khi coupon create:** seed:
   - `coupon:{code}:remaining = totalQuota`
   - `coupon:{code}:eligible-users` set (hoặc dùng `coupon:{code}:eligibility-rule` text rule)
   - `coupon:{code}:meta-discount = ...`
   - `coupon:{code}:meta-discount-type = ...`
4. **Expose endpoint validate:** `GET /api/v1/promotion/campaigns/{id}/validate?userId&items`
5. **Reconciliation job (eventual consistency):** đọc Redis used-users set → persist vào DB; cron daily

Coordinate trong Promotion team task (out of scope của TASK-08; create separate ticket `feat/promotion-redis-coordination`).

### Shared.Contract — events có thể bỏ

Sau khi rewrite, các events sau **không còn dùng** trong Sales flow:
- `ClaimCouponRequestedV1`, `CouponClaimedV1`, `CouponClaimFailedV1`, `CouponReleaseRequestedV1` — chỉ Normal flow vẫn dùng nếu coupon flow của Normal vẫn event-based. Hoặc cũng migrate Normal sang Redis Lua — tùy.

Verify với TASK-07 (Normal saga): nếu cả 2 saga đều dùng `ICouponLockService` Redis → có thể delete 4 events trên ở task cleanup riêng (defer sang sprint sau).

**Plan này** (TASK-08): chỉ thay đổi **Sales saga** dùng Redis Lua. **Normal saga** (TASK-07) giữ event-based `ClaimCouponRequestedV1` cho coupon (đơn giản hơn). Có thể đồng bộ về sau.

### EF Configuration + Migration

**`Order.Persistence/Configurations/PlaceSalesOrderSagaStateConfiguration.cs`** — thêm column config cho fields mới (SaleDiscount, CouponDiscount, SaleStartAt/EndAt, ExpectedTotal, FinalTotal, PaymentUrl, QrCodeUrl, PaymentExpiresAt, ShippingAddressJson, CustomerEmail/Name/Phone/Note, ValidationError, VariantsJson).

Migration tổng hợp ở TASK-12.

## Acceptance Criteria

- [ ] Build OK
- [ ] Unit tests:
  - Handler: pending limit OK → flash sale gate OK → publish event, return ticketId
  - Handler: flash sale sold out → return 409 + slot rollback (không decrement)
  - Handler: pending limit hit → return 429
  - Saga happy: Validating → SaleValidating → CouponLocking → OrderPersisting → InventoryReserving → PaymentSessionCreating → PaymentPending → Final (with hard deduct coupon)
  - Saga without coupon: skip CouponLocking → OrderPersisting
  - Validation fail (price mismatch) → restore flash sale stock + Cancel
  - Sale eligibility fail → restore stock + Cancel
  - Coupon lock fail → restore stock + Cancel
  - Inventory reserve fail → release coupon + restore stock + Cancel
  - Payment timeout → release inventory + release coupon + restore stock + Cancel
- [ ] Integration tests:
  - Flash sale stock decrement đúng (Redis test container)
  - Coupon lock + confirm use + release flow đúng
  - Server-side pricing recalc — client gửi expectedTotal lệch >1% → reject
- [ ] Lua script atomicity test: 100 concurrent flash sale reserve → tổng stock decrement chính xác, no over-sell

## Risk

| Risk | Mitigation |
|---|---|
| Promotion team không kịp seed Redis | Fallback: lazy-init nếu key không tồn tại, gọi Promotion HTTP để load + cache |
| Redis crash → mất state lock/stock | Acceptable: dev project; production cần Redis cluster + persistence (AOF) |
| Coupon eligible-users set quá lớn | Nếu > 10k users → dùng rule-based (`coupon:{code}:rule = "ALL"` hoặc "NEW_USERS") thay vì pre-populate set |

## DoD

- [ ] All tests pass
- [ ] Promotion team coordination ticket created + tracked
- [ ] PR merge
- [ ] Unblock TASK-12, TASK-13
