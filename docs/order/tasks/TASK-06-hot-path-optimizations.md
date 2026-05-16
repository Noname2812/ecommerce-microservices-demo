# TASK-06 · Hot-path Optimizations cho Flash Sale

| | |
|---|---|
| **Effort** | ~1.5 ngày |
| **Depends on** | — (parallel với TASK-02..05) |
| **Blocks** | TASK-08 (docs) |
| **Branch** | `feat/saga/task-06-hot-path-optim` |

## Goal

3 cải tiến độc lập để hardened hot path flash sale:

1. **Pre-warm Redis quota key** khi promotion activated — tránh cold-start Lua INCR race.
2. **Idempotency guard fail-closed** (thay fail-open hiện tại) — tránh quota burn duplicate khi Redis blip.
3. **Gateway-level rate limit** cho `/api/v1/orders/sales` — token bucket chặn global burst trước khi đến Order service.

## Context

### Issue 1: Cold-start Lua INCR race

Hiện tại Lua script `ClaimSlotScript` (PromotionRepository) tự init Redis key nếu chưa tồn tại (`SET NX`). Nhưng dưới 100x burst:
- N concurrent claims hit Redis cùng lúc → tất cả thấy key missing → race tại `SET NX` → 1 winner, N-1 retry.
- Lãng phí Redis cycles + tăng latency tail.

**Fix**: Khi promotion activate, set key trước với value = `TotalSlots`. Lua chỉ DECR.

### Issue 2: Idempotency fail-open

[PlaceSalesOrderCommandHandler.cs:55-58](../../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs#L55-L58):

```csharp
try { /* check cache */ }
catch { /* silent — proceed */ }
```

→ Redis blip → guard skip → quota burn duplicate khi client retry. Với flash sale, double-burn → refund pain.

**Fix**: Reject request với 503 thay vì proceed.

### Issue 3: No global rate limit

Hiện tại có `PlaceOrderRateLimitMiddleware` 5/min per user. Nhưng global burst (10k users × 1 request/sec) vẫn flood Order service. Gateway YARP có `RateLimiterPolicy` support — chưa apply cho sales endpoint.

**Fix**: Token bucket 100/s burst, 50/s sustained per IP.

## Files

### Modified

1. `src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/ActivatePromotion/ActivatePromotionCommandHandler.cs`
2. `src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs` (chỉ dòng 55-58)
3. `src/Gateway/UrbanX.Gateway/appsettings.json`
4. `src/Gateway/UrbanX.Gateway/Program.cs` (nếu cần register rate limit policy)

## Implementation

### 1. Pre-warm Redis quota

Trong `ActivatePromotionCommandHandler.cs`, sau khi save promotion với status=Active:

```csharp
// Đoạn cuối handler, sau SaveChanges
if (promotion.Type == PromotionType.FlashSale)
{
    foreach (var item in promotion.FlashSaleItems)
    {
        var key = $"promotion:flash:{promotion.Id}:item:{item.SlotKey}:slots";
        var remaining = item.TotalSlots - item.SlotsReserved;
        await cacheService.StringSetAsync(
            key,
            remaining.ToString(),
            TimeSpan.FromHours(promotion.DurationHours ?? 48), // hoặc EndsAt - now
            ct);

        _logger.LogInformation(
            "Pre-warmed flash sale quota key {Key} with {Remaining} slots",
            key, remaining);
    }
}
```

> **Lưu ý**: Nếu Promotion có Pause → Activate cycle, key đã exist với value từ trước (lock-step DB ↔ Redis). Logic này vẫn idempotent — `StringSetAsync` overwrite OK vì `SlotsReserved` trên DB là source of truth.

### 2. Idempotency guard fail-closed

```csharp
// PlaceSalesOrderCommandHandler.cs dòng 46-58 hiện tại:
try
{
    var cachedId = await cache.GetStringAsync(guardKey, ct);
    if (!string.IsNullOrEmpty(cachedId))
        return Result.Success(Guid.Parse(cachedId));
}
catch
{
    // silent — proceed
}

// SỬA THÀNH:
try
{
    var cachedId = await cache.GetStringAsync(guardKey, ct);
    if (!string.IsNullOrEmpty(cachedId))
        return Result.Success(Guid.Parse(cachedId));
}
catch (Exception ex)
{
    _logger.LogError(ex, "Idempotency guard cache unavailable for {Key}", guardKey);
    return Result.Failure<Guid>(new Error(
        "SALES_ORDER_GUARD_UNAVAILABLE",
        "Service temporarily unavailable, please retry"));
}
```

Map error code → HTTP 503 trong `ToOrderResult`:

```csharp
// OrderApis.cs hoặc ApiEndpoint.cs
"SALES_ORDER_GUARD_UNAVAILABLE" => Results.Problem(
    statusCode: 503,
    title: "Service Unavailable",
    detail: error.Message)
```

### 3. Gateway rate limit

`appsettings.json`:

```jsonc
{
  "ReverseProxy": {
    "Routes": {
      "orders-sales": {
        "ClusterId": "order-cluster",
        "Match": { "Path": "/api/v{version}/orders/sales" },
        "RateLimiterPolicy": "sales-burst",
        "Transforms": [/* existing */]
      },
      "orders-default": {  // existing — không thay đổi
        "ClusterId": "order-cluster",
        "Match": { "Path": "/api/v{version}/orders/{**catch-all}" }
      }
    }
  },
  "RateLimits": {
    "Policies": {
      "sales-burst": {
        "PermitLimit": 100,
        "Window": "00:00:01",
        "QueueLimit": 0,        // reject immediately
        "QueueProcessingOrder": "OldestFirst"
      }
    }
  }
}
```

`Program.cs`:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddTokenBucketLimiter("sales-burst", o =>
    {
        o.TokenLimit = 100;
        o.TokensPerPeriod = 50;
        o.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
        o.QueueLimit = 0;
        o.AutoReplenishment = true;
    });

    options.RejectionStatusCode = 429;
});

app.UseRateLimiter();
```

> **Partition by IP**: nếu muốn rate limit per-IP, dùng `RateLimitPartition.GetTokenBucketLimiter` với `partitionKey = context.Connection.RemoteIpAddress`.

## Implementation rules

1. **Pre-warm**:
   - Chỉ áp cho `PromotionType.FlashSale`.
   - TTL = thời gian campaign còn hiệu lực (đừng để 24h cứng).
   - `StringSetAsync` overwrite OK — DB `SlotsReserved` là source of truth.
2. **Fail-closed**:
   - Log warning level (không error) — Redis có thể blip nhẹ.
   - Trả status 503 chứ KHÔNG trả error business.
   - Client thấy 503 → retry sau backoff.
3. **Rate limit**:
   - Token bucket > fixed window cho burst-tolerant flash sale.
   - Partition theo IP (default) — không partition theo userId (chưa có JWT validation ở gateway level cho rate limit).
   - Sustainable rate phải ≥ throughput Order service xử lý (50 req/s × 1 order service instance = 50 orders/s).

## Out of scope (chuyển task riêng sau load test)

- **Hot-key sharding** (Phase 5.4 trong plan gốc): chia 1 quota key thành N shards theo `murmur3(userId) % N`. Chỉ áp dụng khi load test xác nhận single-key bottleneck. Tạo task riêng sau khi có data.
- **RabbitMQ tuning toàn bộ bus**: bus-level `UseMessageRetry` policy (hiện tại để consumer-level cấu hình per-endpoint, TASK-03/04 đã handle).

## Acceptance criteria

### Pre-warm
- [ ] Activate flash sale promotion → kiểm tra Redis có key `urbanx:promotion:flash:{id}:item:{slotKey}:slots` value = `TotalSlots - SlotsReserved`, TTL > 0.
- [ ] Activate khi DB `SlotsReserved` > 0 (campaign pause→activate) → key value = `TotalSlots - SlotsReserved` (không reset).
- [ ] Test concurrent 50 claims ngay sau activate → không thấy lỗi "key missing" trong Lua trace.

### Fail-closed
- [ ] Tắt Redis (stop container) → POST `/api/v1/orders/sales` → response 503 `SALES_ORDER_GUARD_UNAVAILABLE`.
- [ ] Bật Redis lại → request kế hoạt động bình thường.
- [ ] Verify log có warning với key + exception type.

### Rate limit
- [ ] Burst 200 req/s tới `/api/v1/orders/sales` → gateway throttle về ~100/s, requests vượt nhận 429.
- [ ] Sustained 50 req/s từ 1 IP → không bị 429.
- [ ] Khác IP không bị block lẫn nhau.

## Testing notes

- Pre-warm test: dùng `IConnectionMultiplexer` trực tiếp để verify Redis key sau activate command.
- Fail-closed test: mock `IDistributedCache` throw `RedisConnectionException`.
- Rate limit test: viết k6/NBomber script (hoặc curl loop) — không bắt buộc viết test code, manual verify OK.

## Reference

- Activate handler: [src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/ActivatePromotion/ActivatePromotionCommandHandler.cs](../../../src/Services/Promotion/UrbanX.Promotion.Application/Usecases/V1/Command/ActivatePromotion/ActivatePromotionCommandHandler.cs)
- Idempotency guard: [src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs:46-58](../../../src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/PlaceSalesOrder/PlaceSalesOrderCommandHandler.cs#L46-L58)
- Rate limit middleware (existing per-user): [src/Services/Order/UrbanX.Order.API/Middleware/PlaceOrderRateLimitMiddleware.cs](../../../src/Services/Order/UrbanX.Order.API/Middleware/PlaceOrderRateLimitMiddleware.cs)
- Gateway YARP config: [src/Gateway/UrbanX.Gateway/appsettings.json](../../../src/Gateway/UrbanX.Gateway/appsettings.json)
- [.NET 8 Rate Limiter docs](https://learn.microsoft.com/aspnet/core/performance/rate-limit)
