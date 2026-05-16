# TASK-05 · PlaceSalesOrder API Refactor (202 Accepted)

| | |
|---|---|
| **Effort** | ~2 ngày |
| **Depends on** | TASK-02, TASK-03, TASK-04 |
| **Blocks** | TASK-08 (docs sync) |
| **Branch** | `feat/saga/task-05-sales-api-refactor` |

## Goal

Đổi API contract `POST /api/v1/orders/sales` từ `201 Created` (sync) sang `202 Accepted` (async). Handler chỉ làm sync portion + publish trigger event, saga handle phần còn lại async. Thêm status endpoint cho client poll.

## Context

Handler hiện tại làm 7 bước sync ([PlaceSalesOrderCommandHandler.cs](../../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs)):
1. Auth check
2. Idempotency guard (Redis)
3. Eligibility validator
4. Quota gate (Redis Lua)
5. Business validators
6. **Promotion redeem (HTTP)** ← chuyển sang saga
7. **Inventory reserve (HTTP)** ← chuyển sang saga
8. **Coupon claim (HTTP)** ← chuyển sang saga
9. Save Order(Confirmed) + outbox event

Sau refactor handler chỉ giữ bước 1-5 + save Order(**Pending**) + publish `PlaceSalesOrderRequestedV1`. Saga (TASK-02) xử lý bước 6-8 async qua RabbitMQ.

User được phép **xóa toàn bộ logic cũ** (HTTP calls + compensation cho promotion/inventory/coupon) vì giờ saga sở hữu state.

## Files

### Modified

1. `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs`
   - **Xóa** dòng 78-148 (Promotion redeem + Inventory reserve + Coupon claim)
   - **Xóa** `order.SetConfirmedAsSalesOrder(...)` — thay bằng tạo Order với `status = Pending`
   - **Xóa** outbox event `PlaceSalesOrderConfirmedV1` ở handler (saga publish)
   - **Thêm** outbox publish `PlaceSalesOrderRequestedV1`
2. `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCompensationBehavior.cs`
   - **Xóa** compensation cho inventory/coupon/promotion (saga handle)
   - **Giữ** chỉ `SaleQuotaReleaseRequestedV1` (rollback quota burn nếu save Order fail)
3. `src/Services/Order/UrbanX.Order.API/Apis/OrderApis.cs` dòng 49-65
   - Đổi `PlaceSalesOrder` trả `Results.Accepted(...)`
   - Thêm route `MapGet("sales/{id:guid}/status", GetSalesOrderStatus)`
4. `src/Services/Order/UrbanX.Order.API/Program.cs`
   - Register saga state machine với EF repository (xem TASK-02 snippet)
5. `src/Services/Order/UrbanX.Order.Application/Clients/IPromotionServiceClient.cs` + Refit impl
   - **Có thể remove** `RedeemAsync`, `CheckCampaignEligibilityAsync`, `GetSalePricesAsync` nếu không dùng ở PlaceOrder normal. **Cẩn thận: PlaceOrder normal có dùng `RedeemAsync`** — giữ lại method đó, chỉ remove ones unused.

### New

6. `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Query/GetSalesOrderStatus/GetSalesOrderStatusQuery.cs`
7. `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Query/GetSalesOrderStatus/GetSalesOrderStatusQueryHandler.cs`

## Implementation

### 1. Handler refactor

```csharp
internal sealed class PlaceSalesOrderCommandHandler(
    IOrderRepository orderRepo,
    IUserContext userContext,
    IDistributedCache cache,
    ISaleAllocationGate allocationGate,
    ISaleEligibilityValidator eligibilityValidator,
    IProductsBusinessValidator productValidator,
    IShippingAddressBusinessValidator shippingValidator,
    ISalePricingBusinessValidator salePricingValidator,
    IOutboxWriter outboxWriter,
    PlaceOrderCompensationContext orderCompensationContext,    // optional, có thể remove
    PlaceSalesOrderCompensationContext salesCompensationContext)
    : ICommandHandler<PlaceSalesOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceSalesOrderCommand cmd, CancellationToken ct)
    {
        // 1. Auth
        var userId = userContext.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result.Failure<Guid>(AuthorizationErrors.Forbidden);

        // 2. Idempotency guard (fail-closed — TASK-06 fixes this)
        var guardKey = PlaceSalesOrderIdempotencyCacheKeys.GuardKey(cmd.IdempotencyKey);
        var cachedId = await cache.GetStringAsync(guardKey, ct);
        if (!string.IsNullOrEmpty(cachedId))
            return Result.Success(Guid.Parse(cachedId));

        // 3. Eligibility
        var eligibility = await eligibilityValidator.ValidateAsync(cmd.CampaignId, userId, cmd.Items, ct);
        if (eligibility.IsFailure) return Result.Failure<Guid>(eligibility.Error);

        // 4. Quota gate
        var totalQty = cmd.Items.Sum(i => i.Quantity);
        var quotaResult = await allocationGate.TryReserveAsync(cmd.CampaignId, userId, totalQty, ct);
        if (quotaResult.IsFailure) return Result.Failure<Guid>(quotaResult.Error);
        salesCompensationContext.SetSaleAllocation(cmd.CampaignId, userId, totalQty, quotaResult.Value.QuotaKey);

        // 5. Business validators (parallel — existing logic)
        var validation = await RunBusinessValidatorsAsync(cmd, ct);
        if (validation.IsFailure) return Result.Failure<Guid>(validation.Error);

        // 6. Save Order(Pending) + outbox trigger event
        var order = Order.CreatePendingSalesOrder(
            userId: userId,
            campaignId: cmd.CampaignId,
            idempotencyKey: cmd.IdempotencyKey,
            shippingAddress: cmd.ShippingAddress,
            shippingFee: cmd.ShippingFee,
            items: cmd.Items,
            customerEmail: cmd.CustomerEmail,
            customerNote: cmd.CustomerNote);

        orderRepo.Add(order);

        await outboxWriter.WriteAsync(new PlaceSalesOrderRequestedV1
        {
            OrderId = order.Id,
            UserId = userId,
            CampaignId = cmd.CampaignId,
            IdempotencyKey = cmd.IdempotencyKey,
            Subtotal = order.Subtotal,
            ShippingFee = order.ShippingFee,
            ShippingAddress = MapShippingAddress(order.ShippingAddress),
            CouponCode = cmd.CouponCode,
            Items = MapItems(order.Items),
            CustomerEmail = cmd.CustomerEmail,
            CustomerNote = cmd.CustomerNote,
            CorrelationId = order.Id.ToString("D")
        }, ct);

        // 7. Set idempotency guard cache (best-effort)
        await SetGuardCacheAsync(guardKey, order.Id, ct);

        return Result.Success(order.Id);
    }
}
```

### 2. Compensation behavior simplify

```csharp
public sealed class PlaceSalesOrderCompensationBehavior : IPipelineBehavior<PlaceSalesOrderCommand, Result<Guid>>
{
    private readonly PlaceSalesOrderCompensationContext _salesCtx;
    private readonly ICompensationOutboxWriter _writer;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PlaceSalesOrderCompensationBehavior> _logger;

    public async Task<Result<Guid>> Handle(...)
    {
        try
        {
            var result = await next();
            if (result.IsFailure)
                await CompensateAsync(result.Error.Message, ct);
            return result;
        }
        catch
        {
            await CompensateAsync("ORDER_SAVE_FAILED", ct);
            throw;
        }
    }

    private async Task CompensateAsync(string reason, CancellationToken ct)
    {
        if (!_salesCtx.HasSaleAllocation) return;

        try
        {
            await _uow.ExecuteInTransactionAsync(async () =>
            {
                await _writer.WriteAsync(new SaleQuotaReleaseRequestedV1
                {
                    CampaignId = _salesCtx.CampaignId!.Value,
                    UserId = _salesCtx.UserId!,
                    Quantity = _salesCtx.Quantity!.Value,
                    QuotaKey = _salesCtx.QuotaKey!,
                    Reason = Sanitize(reason)
                }, ct);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write quota release compensation for {QuotaKey}", _salesCtx.QuotaKey);
        }
    }
}
```

**Có thể xóa**: toàn bộ `PlaceOrderCompensationContext` injection trong saga flow + writes về inventory/coupon/flash-sale (saga handle).

### 3. Endpoint changes

```csharp
// OrderApis.cs
private static async Task<IResult> PlaceSalesOrder(
    PlaceSalesOrderCommand cmd, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(cmd, ct);
    return result.IsSuccess
        ? Results.Accepted(
            uri: $"{BaseURL}/sales/{result.Value}/status".Replace("{version:apiVersion}", "1"),
            value: new { orderId = result.Value, status = "Pending" })
        : ToOrderResult(result);
}

// AddRoutes
v1.MapPost("sales", PlaceSalesOrder);
v1.MapGet("sales/{id:guid}/status", GetSalesOrderStatus);

private static async Task<IResult> GetSalesOrderStatus(
    Guid id, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(new GetSalesOrderStatusQuery(id), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : ToOrderResult(result);
}
```

### 4. Status query

```csharp
[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
public record GetSalesOrderStatusQuery(Guid OrderId) : IQuery<SalesOrderStatusDto>;

public sealed class GetSalesOrderStatusQueryHandler(
    OrderDbContext dbContext,
    IUserContext userContext)
    : IQueryHandler<GetSalesOrderStatusQuery, SalesOrderStatusDto>
{
    public async Task<Result<SalesOrderStatusDto>> Handle(GetSalesOrderStatusQuery q, CancellationToken ct)
    {
        var data = await (
            from o in dbContext.Orders.AsNoTracking()
            join s in dbContext.PlaceSalesOrderSagas.AsNoTracking() on o.Id equals s.OrderId into sj
            from s in sj.DefaultIfEmpty()
            where o.Id == q.OrderId
            select new { Order = o, Saga = s }
        ).FirstOrDefaultAsync(ct);

        if (data == null)
            return Result.Failure<SalesOrderStatusDto>(OrderErrors.NotFound(q.OrderId));

        // Ownership check
        if (data.Order.UserId != userContext.UserId)
            return Result.Failure<SalesOrderStatusDto>(AuthorizationErrors.Forbidden);

        return Result.Success(new SalesOrderStatusDto(
            OrderId: data.Order.Id,
            OrderStatus: data.Order.Status.ToString(),
            SagaState: data.Saga?.CurrentState ?? "Pending",
            ReservationId: data.Saga?.ReservationId,
            CouponClaimId: data.Saga?.CouponClaimId,
            FailureStep: data.Saga?.FailureStep,
            FailureReason: data.Saga?.FailureReason,
            UpdatedAt: data.Saga?.UpdatedAt ?? data.Order.CreatedAt));
    }
}

public record SalesOrderStatusDto(
    Guid OrderId,
    string OrderStatus,
    string SagaState,
    Guid? ReservationId,
    Guid? CouponClaimId,
    string? FailureStep,
    string? FailureReason,
    DateTimeOffset UpdatedAt);
```

> **Optional**: dùng `[CacheQuery]` attribute với TTL 1s để giảm DB load khi client poll.

### 5. Saga registration (Program.cs)

```csharp
builder.Services.AddMessaging(configureBus: bus =>
{
    bus.AddSagaStateMachine<PlaceSalesOrderSagaStateMachine, PlaceSalesOrderSagaState>(cfg =>
    {
        cfg.UseInMemoryOutbox();
    })
    .EntityFrameworkRepository(r =>
    {
        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        r.ExistingDbContext<OrderDbContext>();
    });
});
```

## Allowed cleanup

User cho phép xóa logic cũ không hợp lý. Đề xuất xóa:

- **In handler**:
  - HTTP client invocations to Promotion/Inventory/Coupon (dòng 78-148).
  - `PlaceOrderCompensationContext` references trong PlaceSalesOrder (giờ saga sở hữu).
  - `salesCompensationContext.SetXxxClaimedFlashSaleSlots(...)` cho promotion (saga handle).
- **In compensation behavior**:
  - Inventory/Coupon/FlashSale slot release branches.
  - Inject `PlaceOrderCompensationContext`.
- **In DI** ([ServiceCollectionExtensions.cs](../../../src/Services/Order/UrbanX.Order.Application/DependencyInjection/Extensions/ServiceCollectionExtensions.cs)):
  - Nếu các compensation context không dùng ở PlaceOrder normal, có thể move scope hoặc remove.
- **In clients** (`IPromotionServiceClient`, `IInventoryClient`, `ICouponClient`):
  - Methods chỉ dùng cho PlaceSalesOrder (vd `CheckCampaignEligibilityAsync` nếu chỉ flash sale dùng) — verify usage rồi remove.

> ⚠️ **Cẩn thận**: PlaceOrder normal vẫn dùng `IPromotionServiceClient.RedeemAsync`, `IInventoryClient.ReserveAsync`, `ICouponClient.ClaimAsync`. Đừng remove methods/interfaces — chỉ remove invocations từ PlaceSalesOrder.

## Acceptance criteria

- [ ] Build thành công.
- [ ] POST `/api/v1/orders/sales` trả `202 Accepted` + body `{ orderId, status: "Pending" }` + header `Location: /api/v1/orders/sales/{id}/status`.
- [ ] GET status endpoint return state đúng từng giai đoạn (test với saga harness E2E).
- [ ] Save Order trong cùng EF transaction với outbox write `PlaceSalesOrderRequestedV1` (kiểm tra qua transaction log).
- [ ] Compensation behavior: nếu save Order fail mà quota đã burn → `SaleQuotaReleaseRequestedV1` được publish.
- [ ] Quota gate (Redis Lua) + idempotency guard chạy đúng thứ tự (idempotency first).
- [ ] PlaceOrder normal endpoint **không bị ảnh hưởng** (regression test happy path).
- [ ] Unit test cover: happy path return order id, eligibility fail, quota exhausted, validator fail, compensation triggered.

## Testing notes

- Integration test với Aspire `DistributedApplicationTestingBuilder`:
  - POST sales order → 202.
  - Poll GET status mỗi 200ms — assert eventual transition tới `Confirmed` (E2E qua saga + mock consumers).
- Test ownership: user A POST → user B GET status → 403.

## Reference

- Current handler: [src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs](../../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs)
- Current endpoint: [src/Services/Order/UrbanX.Order.API/Apis/OrderApis.cs](../../../src/Services/Order/UrbanX.Order.API/Apis/OrderApis.cs)
- Current API doc (sẽ deprecate sau migration): [docs/order/place-sales-order.md](../place-sales-order.md)
