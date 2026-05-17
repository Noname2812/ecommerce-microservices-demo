# TASK-06 — Order Handler Async (Return 202)

**Team:** Order · **Effort:** M (1.5d) · **Depends:** TASK-01, TASK-02, TASK-04
**Branch:** `feature/order-refactor/TASK-06-handler-async`

## Mục đích

Refactor 2 command handlers thành async:

**Normal handler** (`PlaceOrderCommandHandler`):
- FluentValidation + check Redis pending slot (Normal counter) + publish ticket event + return 202

**Sales handler** (`PlaceSalesOrderCommandHandler`):
- FluentValidation + check Redis pending slot (Sales counter) + **Flash sale gate (Redis Lua atomic DECRBY stock)** + publish ticket event + return 202

⚠ **Sales handler có Flash sale gate** = early reject `409 Sold Out` nếu hết quota Redis. Chi tiết flow Flash sale + Coupon ở [TASK-08](TASK-08-saga-sales.md).

KHÔNG còn save Order ngay; saga đảm nhận tất cả validate/save/reserve/coupon/payment.

## Files

### Rewrite `PlaceOrderCommandHandler.cs` (Normal)
```csharp
using MassTransit;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCommandHandler(
    IPublishEndpoint publishEndpoint,
    IPendingOrderSlotService pendingSlots,
    IUserContext userContext)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var slot = await pendingSlots.TryAcquireAsync(userId, OrderType.Normal, ct);
        if (slot.IsFailure)
            return Result.Failure<Guid>(slot.Error);

        var ticketId = Guid.NewGuid();
        await publishEndpoint.Publish(new PlaceOrderRequestedV1
        {
            OrderId         = ticketId,
            UserId          = userId.ToString("D"),
            IdempotencyKey  = cmd.IdempotencyKey,
            CouponCode      = cmd.CouponCode,
            ShippingAddress = MapShipping(cmd.ShippingAddress),
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

    private static ShippingAddressSnapshot MapShipping(/* cmd type */) =>
        new(/* ... */);
}
```

⚠ Bỏ inject: `IOutboxWriter`, `IProductValidator`, `IShippingValidator`, `IPricingValidator`, `ICatalogSnapshotReader`, `IOrderRepository`.

### Rewrite `PlaceSalesOrderCommandHandler.cs` (Sales — có Flash sale gate)

```csharp
public sealed class PlaceSalesOrderCommandHandler(
    IPublishEndpoint publishEndpoint,
    IPendingOrderSlotService pendingSlots,
    IFlashSaleStockService flashSaleStock,                  // NEW — TASK-08 service
    IUserContext userContext)
    : ICommandHandler<PlaceSalesOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceSalesOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        // [2] Pending limit — Sales counter (default max 3)
        var slot = await pendingSlots.TryAcquireAsync(userId, OrderType.Sales, ct);
        if (slot.IsFailure)
            return Result.Failure<Guid>(slot.Error);

        // [3] Flash sale gate — atomic DECRBY Redis stock
        var totalQty = cmd.Items.Sum(i => i.Quantity);
        var stockResult = await flashSaleStock.TryReserveAsync(cmd.CampaignId, totalQty, ct);
        if (stockResult.IsFailure)
        {
            await pendingSlots.ReleaseAsync(userId, ct);    // rollback slot
            return Result.Failure<Guid>(stockResult.Error); // 409 FlashSaleSoldOut
        }

        var ticketId = Guid.NewGuid();
        await publishEndpoint.Publish(new PlaceSalesOrderRequestedV1
        {
            OrderId         = ticketId,
            UserId          = userId.ToString("D"),
            CampaignId      = cmd.CampaignId,                // NEW
            IdempotencyKey  = cmd.IdempotencyKey,
            CouponCode      = cmd.CouponCode,
            ExpectedTotal   = cmd.ExpectedTotal,             // NEW — saga verify 1% tolerance
            ShippingAddress = MapShipping(cmd.ShippingAddress),
            ShippingFee     = cmd.ShippingFee,
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

⚠ **Quan trọng**: Flash sale gate Redis decrement TRƯỚC publish event. Nếu publish fail sau khi đã decrement → cần rollback stock. Try/catch pattern hoặc dùng IPublishEndpoint enlisted vào TX. Simple impl: try/catch:
```csharp
try {
    await publishEndpoint.Publish(...);
} catch {
    await flashSaleStock.RestoreAsync(cmd.CampaignId, totalQty, ct);
    await pendingSlots.ReleaseAsync(userId, ct);
    throw;
}
```

### Modify `Order.API/Apis/OrderApis.cs`
```csharp
private static async Task<IResult> Create(
    PlaceOrderCommand cmd, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(cmd, ct);
    return result.IsSuccess
        ? Results.Accepted($"{BaseURL}/ticket/{result.Value}", new { ticketId = result.Value })
        : ToOrderResult(result);
}

private static async Task<IResult> CreateSales(
    PlaceSalesOrderCommand cmd, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(cmd, ct);
    return result.IsSuccess
        ? Results.Accepted($"{BaseURL}/ticket/{result.Value}", new { ticketId = result.Value })
        : ToOrderResult(result);
}
```

Verify `ApiEndpoint.ToOrderResult` map:
- `Order.TooManyPending` → 429
- `Order.Forbidden` → 403
- `Order.CatalogValidationFailed` → 400

### Modify `PlaceOrderCommand.cs` (Application/Usecases/V1/Command/PlaceOrder/)
- Verify validator: chỉ giữ rules in-memory (item count ≤ 20, qty ≤ 100, address required, idempotency key UUID format). Bỏ rule pricing snapshot 30-min window (logic đó chuyển vào saga validate step).

### Modify `PlaceSalesOrderCommand.cs`
- Similar: in-memory rules only (item count ≤ 10, qty ≤ 5, idempotency key, campaignId not empty, **`ExpectedTotal > 0`**).
- ⚠ Field mới: `decimal ExpectedTotal` — client gửi finalTotal kỳ vọng. Saga verify lệch ≤ 1% so với server-calc (xem TASK-08).

### `OrderFactory.cs` — REFACTOR (vẫn keep, dùng cho saga TASK-07/08)
- Đổi signature: `BuildFromSaga(PlaceOrderNormalSagaState saga, IDictionary<Guid, CatalogVariantInfo> variants, Guid orderId)`
- Build Order entity với `Status=Processing`
- Items lấy từ `saga.ItemsJson` + enrich với `CatalogVariantInfo` (productName, variantSku, sellerId, sellerName, imageUrl)
- Tính Subtotal, TaxAmount, TotalAmount, FinalAmount giống logic cũ

## Acceptance Criteria

### Normal handler
- [ ] Build OK
- [ ] POST `/api/v1/order/orders` với valid payload → **202** trong < 100ms với `{ ticketId: <guid> }`
- [ ] Verify response include `Location` header `/api/v1/order/orders/ticket/{ticketId}`
- [ ] Unit tests:
  - Valid input → return ticketId, publish event, slot incremented
  - Pending limit hit (MaxNormalPending=1) → return 429 Failure, slot không tăng
  - User unauthenticated → return 403 Failure
- [ ] Integration: POST 2 lần (Normal) → lần 2 → 429 `Order.TooManyPending`

### Sales handler
- [ ] POST `/api/v1/order/sales-orders` → **202** < 100ms với `{ ticketId }`
- [ ] Unit tests:
  - Valid + stock đủ → return ticketId, slot Sales=1, Redis flash sale stock decrement
  - Stock không đủ → return 409 `Order.FlashSaleSoldOut`, slot KHÔNG tăng (rollback)
  - Sales pending limit hit (MaxSalesPending=3) → return 429 Failure
- [ ] Integration:
  - POST 4 lần Sales liên tiếp → lần 4 → 429
  - POST với campaignId không có Redis stock → 409 (stock = 0)
  - POST với quantity > remaining stock → 409
- [ ] Race test: 10 concurrent POST cho cùng campaign với stock=5 → exactly 5 success, 5 reject 409, no over-sell

## Notes

- HttpIdempotency middleware (`AddHttpIdempotency`) đã có ở `Program.cs:27` — user gửi header `Idempotency-Key` cùng request → middleware cache response → next request cùng key trả cached
- Saga TASK-07/08 sẽ consume event và làm tiếp validate/save/reserve
- `userContext.Email/FullName/Phone` — verify `IUserContext` đã có các property này (Gateway forward qua header); nếu chưa → coordinate Shared/Platform team
- **`IFlashSaleStockService`** — sẽ implement chi tiết ở TASK-08; trong TASK-06 chỉ inject interface
- **Idempotency Flash sale gate**: nếu user POST 2 lần cùng `Idempotency-Key`, HttpIdempotency middleware sẽ return cached response → handler KHÔNG được gọi lần 2 → Redis stock chỉ decrement 1 lần. ✅ Đảm bảo no double-decrement nếu client gửi đúng key.

## DoD

- [ ] All tests pass
- [ ] PR review + merge
- [ ] Unblock TASK-10
