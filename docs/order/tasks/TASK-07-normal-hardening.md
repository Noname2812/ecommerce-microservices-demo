# TASK-07 · PlaceOrder Normal Hardening

| | |
|---|---|
| **Effort** | ~1 ngày |
| **Depends on** | — (parallel) |
| **Blocks** | TASK-08 (docs) |
| **Branch** | `feat/saga/task-07-normal-hardening` |

## Goal

PlaceOrder normal vẫn giữ sync orchestration (không refactor sang saga) nhưng cần 3 cải tiến cho production:

1. **Polly tuning** — Promotion HTTP timeout infinite → 5s; Catalog client chưa có resilience.
2. **Coupon `UsedQuota` atomic SQL** — fix drift counter khi EF in-memory increment + Redis quota DECR race.
3. **Observability** — OTel spans + metrics cho mỗi orchestration step.

## Context

### Issue 1: Promotion infinite timeout

[ServiceCollectionExtensions.cs:103-108](../../../src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs#L103-L108):

```csharp
services.AddHttpClient<IPromotionServiceClient, PromotionServiceClient>(client =>
{
    client.BaseAddress = ...;
    client.Timeout = Timeout.InfiniteTimeSpan;  // ⚠️ infinite
})
.AddStandardResilienceHandler();
```

Nếu Promotion 5xx hoặc hang → toàn bộ PlaceOrder thread block. `AddStandardResilienceHandler` có circuit breaker nhưng `Timeout.InfiniteTimeSpan` vô hiệu hoá timeout layer.

### Issue 2: Catalog client thiếu Polly

[ServiceCollectionExtensions.cs:110-113](../../../src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs#L110-L113):

```csharp
services.AddHttpClient<ICatalogClient, CatalogClient>(client => { /* basic */ });
// ❌ Không có AddResilienceHandler hay AddStandardResilienceHandler
```

### Issue 3: Coupon UsedQuota drift

[RedeemPromotionCommandHandler.cs](../../../src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/RedeemPromotion/RedeemPromotionCommandHandler.cs) — sequence:

```
1. Redis: DECR coupon:{code}:quota (atomic Lua)   ← thành công
2. EF in-memory: coupon.UsedQuota++               ← in-memory only
3. EF SaveChanges                                  ← nếu fail (deadlock/conflict)
   → Redis đã DECR, DB không tăng → drift
```

Lần redeem kế đọc DB `UsedQuota` (stale) → quyết định wrong.

### Issue 4: Missing observability

PlaceOrder normal có 4 sync HTTP calls + parallel validators nhưng chỉ có top-level activity. Khi tail latency cao, khó identify step nào chậm.

## Files

### Modified

1. `src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs` — Polly tuning.
2. `src/Services/Promotion/UrbanX.Promotion.Persistence/Repositories/CouponRepository.cs` — atomic SQL update method.
3. `src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/RedeemPromotion/RedeemPromotionCommandHandler.cs` — gọi atomic method.
4. `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceOrder/PlaceOrderCommandHandler.cs` — wrap steps trong Activity span.

### New

5. `src/Services/Order/UrbanX.Order.Application/Observability/OrderActivitySource.cs` — central ActivitySource + meters.

## Implementation

### 1. Polly tuning

```csharp
// Promotion HTTP
services.AddHttpClient<IPromotionServiceClient, PromotionServiceClient>(client =>
{
    client.BaseAddress = new Uri("https+http://promotion-service");
    client.Timeout = TimeSpan.FromSeconds(10);  // overall request timeout
})
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);   // per-attempt
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    o.Retry.MaxRetryAttempts = 2;
    o.Retry.Delay = TimeSpan.FromMilliseconds(200);
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.CircuitBreaker.FailureRatio = 0.5;
    o.CircuitBreaker.MinimumThroughput = 10;
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
});

// Catalog HTTP
services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
{
    client.BaseAddress = new Uri("https+http://catalog-service");
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddResilienceHandler("catalog-retry", builder =>
{
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout)
    });
    builder.AddTimeout(TimeSpan.FromSeconds(3));
});
```

### 2. Atomic Coupon UsedQuota

`CouponRepository.cs` thêm method:

```csharp
public async Task<bool> TryIncrementUsedQuotaAsync(string code, CancellationToken ct)
{
    var affected = await _ctx.Coupons
        .Where(c => c.Code == code &&
                    (c.TotalQuota == null || c.UsedQuota < c.TotalQuota))
        .ExecuteUpdateAsync(s => s.SetProperty(
            c => c.UsedQuota,
            c => c.UsedQuota + 1), ct);

    return affected == 1;
}
```

`RedeemPromotionCommandHandler.cs` — thay đoạn `coupon.UsedQuota++; await repo.UpdateAsync(coupon, ct);` bằng:

```csharp
var incremented = await _couponRepo.TryIncrementUsedQuotaAsync(coupon.Code, ct);
if (!incremented)
{
    // Quota đã exhaust giữa Redis DECR và SQL UPDATE (cực hiếm) → rollback Redis
    await _couponClaimRedisGateway.RestoreQuotaSlotAsync(coupon.Code, ct);
    return Result.Failure<RedeemPromotionResult>(CouponErrors.QuotaExhausted);
}
```

> **Important**: Atomic SQL UPDATE thực hiện trong cùng transaction với phần còn lại (TransactionPipelineBehavior wrap). Vẫn cần `RestoreQuotaSlotAsync` cho edge case Redis-SQL drift (extremely rare nhưng correct).

### 3. Order observability

```csharp
// OrderActivitySource.cs
namespace UrbanX.Order.Application.Observability;

public static class OrderActivitySource
{
    public const string Name = "UrbanX.Order";
    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> PlaceOrderSuccess =
        Meter.CreateCounter<long>("orders.place.success");
    public static readonly Counter<long> PlaceOrderFailure =
        Meter.CreateCounter<long>("orders.place.failure");
    public static readonly Histogram<double> PlaceOrderDuration =
        Meter.CreateHistogram<double>("orders.place.duration", unit: "ms");
}
```

Register vào `ServiceDefaults` OTel setup (tự động pickup ActivitySource theo name).

`PlaceOrderCommandHandler.cs` — wrap mỗi step:

```csharp
public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
{
    using var rootSpan = OrderActivitySource.Source.StartActivity("place-order");
    rootSpan?.SetTag("order.user_id", userContext.UserId);
    rootSpan?.SetTag("order.idempotency_key", cmd.IdempotencyKey);

    var sw = Stopwatch.StartNew();

    try
    {
        // 1. Auth + validators
        using (var span = OrderActivitySource.Source.StartActivity("validate-business-rules"))
        {
            // existing parallel validation
        }

        // 2. Promotion redeem
        using (var span = OrderActivitySource.Source.StartActivity("promotion-redeem"))
        {
            span?.SetTag("order.coupon_code", cmd.CouponCode);
            // existing call
            if (failed) { span?.SetTag("error", true); OrderActivitySource.PlaceOrderFailure.Add(1, new("step", "promotion")); }
        }

        // 3. Inventory reserve
        using (var span = OrderActivitySource.Source.StartActivity("inventory-reserve")) { /* ... */ }

        // 4. Coupon claim
        using (var span = OrderActivitySource.Source.StartActivity("coupon-claim")) { /* ... */ }

        // 5. Save order
        using (var span = OrderActivitySource.Source.StartActivity("save-order")) { /* ... */ }

        OrderActivitySource.PlaceOrderSuccess.Add(1);
        return Result.Success(order.Id);
    }
    finally
    {
        OrderActivitySource.PlaceOrderDuration.Record(sw.Elapsed.TotalMilliseconds);
    }
}
```

## Implementation rules

1. **Polly**:
   - `client.Timeout` (HttpClient level) phải `≥ TotalRequestTimeout` để không cắt trước resilience pipeline.
   - Per-attempt timeout < client timeout < total request timeout.
   - Circuit breaker: 50% failure rate trên 10 requests → break 30s.
2. **Atomic UsedQuota**:
   - Dùng `ExecuteUpdateAsync` (EF Core 7+) — single SQL roundtrip.
   - Trong WHERE clause check `UsedQuota < TotalQuota` (nullable) — atomic conflict resolution.
   - Nếu `affected == 0` → quota exhausted hoặc coupon đã bị deactivate → rollback Redis + error.
3. **Observability**:
   - ActivitySource name = `UrbanX.Order` (constant trong class).
   - Span name dùng `kebab-case`.
   - Tag prefix `order.*` cho consistency.
   - Metric counter dimension `step` để filter Grafana.

## Acceptance criteria

### Polly
- [ ] Promotion endpoint sleep 10s → PlaceOrder normal fail trong ~5s với `HttpRequestException` (per-attempt timeout) hoặc 10s với `TaskCanceledException` (total).
- [ ] Promotion 50% 503 trong 10 requests liên tiếp → circuit breaker mở 30s, request kế hoạt động fast-fail.
- [ ] Catalog 503 → 2 retry với exponential backoff (200ms, 400ms), sau đó fail.

### Atomic Coupon
- [ ] 50 concurrent redeem cùng coupon (quota = 30) → DB `UsedQuota = 30`, Redis quota = 0, đúng 30 success + 20 fail.
- [ ] Redeem khi quota exhaust (UsedQuota == TotalQuota) → `ExecuteUpdateAsync` return 0 → result failure.
- [ ] Test integration: check không có drift sau 1000 concurrent redeems random success/fail.

### Observability
- [ ] Aspire Dashboard trace mỗi PlaceOrder có 5 child span (validate, promotion, inventory, coupon, save).
- [ ] Metric `orders.place.duration` p50/p95/p99 hiển thị.
- [ ] Metric `orders.place.failure{step=...}` counter tăng đúng khi step fail.

## Testing notes

- Polly: dùng `HttpClient` mock với `MockHttpHandler` (gen-mock package) hoặc Aspire integration test mock service.
- Atomic: integration test với real Postgres (xUnit fixture spin up testcontainer), test 50 parallel `TryIncrementUsedQuotaAsync`.
- Observability: chạy AppHost local, mở Aspire Dashboard, manually inspect trace.

## Reference

- HTTP clients DI: [src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs](../../../src/Services/Order/UrbanX.Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs)
- Coupon redeem: [src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/RedeemPromotion/RedeemPromotionCommandHandler.cs](../../../src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/RedeemPromotion/RedeemPromotionCommandHandler.cs)
- Coupon repo: [src/Services/Promotion/UrbanX.Promotion.Persistence/Repositories/CouponRepository.cs](../../../src/Services/Promotion/UrbanX.Promotion.Persistence/Repositories/CouponRepository.cs)
- ServiceDefaults OTel: [src/ServiceDefaults/](../../../src/ServiceDefaults/)
- [.NET 8 Resilience docs](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)
