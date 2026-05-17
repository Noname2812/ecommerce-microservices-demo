# TASK-04 — Pending Order Slot Service (Redis)

**Team:** Order · **Effort:** S (1d) · **Depends:** —
**Branch:** `feature/order-refactor/TASK-04-pending-slot`

## Mục đích

Giới hạn số order PENDING mỗi user qua Redis atomic. Default `MaxPendingPerUser=1`. Handler `TryAcquireAsync` trước khi publish ticket; saga `ReleaseAsync` khi Finalize/Faulted.

## Files NEW

### `Order.Application/DependencyInjection/Options/PlaceOrderOptions.cs`
```csharp
namespace UrbanX.Order.Application.DependencyInjection.Options;

public sealed class PlaceOrderOptions
{
    public const string SectionName = "PlaceOrder";

    public int MaxNormalPendingPerUser { get; init; } = 1;
    public int MaxSalesPendingPerUser  { get; init; } = 3;
    public int PendingSlotTtlMinutes   { get; init; } = 30;
    public int CouponLockTtlSeconds    { get; init; } = 960;     // 16 phút (saga 15p + buffer)
    public decimal PriceMismatchTolerance { get; init; } = 0.01m; // 1%
}
```

⚠ **Khác biệt Normal vs Sales:**
- Normal: max 1 pending/user (hạn chế spam)
- Sales: max 3 pending/user (flash sale linh hoạt hơn, user có thể grab nhiều campaign)

### `Order.Application/Services/IPendingOrderSlotService.cs`
```csharp
namespace UrbanX.Order.Application.Services;

public interface IPendingOrderSlotService
{
    Task<Result> TryAcquireAsync(Guid userId, OrderType orderType, CancellationToken ct);
    Task ReleaseAsync(Guid userId, CancellationToken ct);
}
```

⚠ Method `TryAcquireAsync` nhận thêm `OrderType` để pick đúng `MaxNormalPendingPerUser` hoặc `MaxSalesPendingPerUser`. `ReleaseAsync` không cần (decrement chung 1 counter).

**Key per user:**
- Normal: `{instanceName}:pending-orders:normal:{userId}`
- Sales:  `{instanceName}:pending-orders:sales:{userId}`

→ 2 counter tách biệt, không "cross-pollinate". Normal pending không block Sales pending và ngược lại.

### `Order.Infrastructure/Services/RedisPendingOrderSlotService.cs`
- Inject `ICacheService` (đã có Lua eval method) hoặc `IConnectionMultiplexer`
- Key format: `{InstanceName}:pending-orders:{userId}` — prefix từ `CacheOptions.InstanceName`

**TryAcquire Lua script (atomic):**
```lua
local current = redis.call('INCR', KEYS[1])
if current == 1 then
    redis.call('EXPIRE', KEYS[1], ARGV[1])
end
if current > tonumber(ARGV[2]) then
    redis.call('DECR', KEYS[1])
    return 0
end
return current
```
- `KEYS[1]` = `{instanceName}:pending-orders:{userId}`
- `ARGV[1]` = TTL seconds (`PlaceOrderOptions.PendingSlotTtlMinutes * 60`)
- `ARGV[2]` = max (`PlaceOrderOptions.MaxPendingPerUser`)
- Return `0` → reject (Result.Failure `OrderErrors.TooManyPendingOrders`)
- Return `>0` → success (current slot count)

**Release Lua script (atomic, no underflow):**
```lua
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
if current <= 0 then return 0 end
return redis.call('DECR', KEYS[1])
```

### Service implementation skeleton
```csharp
public sealed class RedisPendingOrderSlotService(
    ICacheService cache,
    IOptions<PlaceOrderOptions> options,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisPendingOrderSlotService> logger)
    : IPendingOrderSlotService
{
    private readonly PlaceOrderOptions _opts = options.Value;
    private readonly string _prefix = $"{cacheOptions.Value.InstanceName}:pending-orders";

    private const string AcquireScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        if current > tonumber(ARGV[2]) then redis.call('DECR', KEYS[1]); return 0 end
        return current
        """;

    private const string ReleaseScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        if current <= 0 then return 0 end
        return redis.call('DECR', KEYS[1])
        """;

    public async Task<Result> TryAcquireAsync(Guid userId, OrderType orderType, CancellationToken ct)
    {
        var (key, max) = orderType switch
        {
            OrderType.Sales => ($"{_prefix}:sales:{userId:D}", _opts.MaxSalesPendingPerUser),
            _               => ($"{_prefix}:normal:{userId:D}", _opts.MaxNormalPendingPerUser),
        };

        var result = await cache.ScriptEvaluateAsync<long>(
            AcquireScript,
            new[] { key },
            new object[] { _opts.PendingSlotTtlMinutes * 60, max },
            ct);

        if (result == 0)
            return Result.Failure(OrderErrors.TooManyPendingOrders);

        return Result.Success();
    }

    public async Task ReleaseAsync(Guid userId, CancellationToken ct)
    {
        // Release cả 2 key (saga không biết orderType khi release; cheap operation)
        await Task.WhenAll(
            cache.ScriptEvaluateAsync<long>(ReleaseScript,
                new[] { $"{_prefix}:normal:{userId:D}" }, Array.Empty<object>(), ct),
            cache.ScriptEvaluateAsync<long>(ReleaseScript,
                new[] { $"{_prefix}:sales:{userId:D}" }, Array.Empty<object>(), ct));
    }
}
```

## DI Registration

### `Order.Application/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`
```csharp
services.AddOptions<PlaceOrderOptions>()
    .BindConfiguration(PlaceOrderOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### `Order.Infrastructure/DependencyInjection/Extensions/ServiceCollectionExtensions.cs`
```csharp
services.AddSingleton<IPendingOrderSlotService, RedisPendingOrderSlotService>();
```

### `Order.API/appsettings.json`
```json
{
  "PlaceOrder": {
    "MaxNormalPendingPerUser": 1,
    "MaxSalesPendingPerUser": 3,
    "PendingSlotTtlMinutes": 30,
    "CouponLockTtlSeconds": 960,
    "PriceMismatchTolerance": 0.01
  }
}
```

## Acceptance Criteria

- [ ] Build OK
- [ ] Unit tests cover:
  - `TryAcquire(Normal)` 1 lần → return Success, Redis key `pending-orders:normal:{userId}` = 1, TTL set
  - `TryAcquire(Normal)` 2 lần với MaxNormal=1 → lần 2 return Failure(TooManyPendingOrders)
  - `TryAcquire(Sales)` 3 lần với MaxSales=3 → tất cả Success; lần 4 → Failure
  - `TryAcquire(Normal)` + `TryAcquire(Sales)` → cả 2 đều Success (counter tách biệt)
  - `Release` decrement đúng cả 2 keys
  - `Release` 2 lần liên tiếp khi slot=1 → slot=0 (không âm, không < 0)
  - `TryAcquire` sau `Release` → return Success
- [ ] Integration test với Redis Aspire local
- [ ] Verify Lua script atomicity (concurrent 100 requests Sales, expected slot ≤ 3)

## Notes

- `ICacheService.ScriptEvaluateAsync<T>` đã có trong `Shared.Cache` — verify signature
- Nếu `ICacheService` chưa có Lua eval generic → bổ sung trong cùng task (touch `Shared.Cache`) — coordinate với Shared/Platform team
- Singleton an toàn vì `IConnectionMultiplexer` thread-safe

## DoD

- [ ] All unit + integration tests pass
- [ ] PR merge
- [ ] Unblock TASK-06
