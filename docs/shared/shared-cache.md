# Shared.Cache — Redis cache + Resilience

`Shared.Cache` cung cấp Redis-backed cache, distributed lock, và circuit breaker dùng chung cho toàn bộ service. Mọi caller (pipeline behavior hay handler thủ công) đều an toàn trước stampede và Redis-die.

---

## Mục đích

- **Cache-aside có stampede protection** — SingleFlight (in-process) + optional distributed lock (cross-process).
- **Graceful degradation khi Redis chết** — `GetAsync`/`SetAsync`/`RemoveAsync` không throw; circuit breaker tự mở sau 5 lỗi liên tiếp và đóng lại khi probe thành công.
- **Distributed lock** — `IDistributedLockService` (SET NX PX, Lua release với token check) cho mutual exclusion + integration với `[DistributedLock]` attribute.
- **Lua eval** — cho phép caller tự chạy Lua script atomic (rate limit, flash sale, coupon claim). Lua eval **vẫn throw** nếu Redis fail — caller phải tự quyết định fallback.

---

## DI registration

```csharp
// Program.cs
builder.AddSharedCache("redis");   // đăng ký IConnectionMultiplexer (Aspire), IDistributedCache,
                                   // ICacheService, IDistributedLockService, RedisCircuitBreaker
```

AppHost phải bind Redis vào service:
```csharp
.WithReference(redis).WaitFor(redis)
```

---

## API

### Basic ops (silent fail-open)
| Method | Hành vi khi Redis fail / circuit open |
|---|---|
| `GetAsync<T>` | Log warn, `RecordFailure`, return `default` (treat as cache miss) |
| `SetAsync<T>` | Log warn, `RecordFailure`, no-op (fire-and-forget) |
| `RemoveAsync` | Log warn, no-op |
| `ExistsAsync` | Return `false` |
| `RemoveByPatternAsync` | Log warn, no-op |

### `GetOrSetAsync` overload mới (stampede-safe)

```csharp
public sealed record GetOrSetOptions<T>
{
    public TimeSpan? Expiry { get; init; }
    public Func<T, TimeSpan>? ExpirySelector { get; init; }   // dynamic TTL
    public bool UseSingleFlight { get; init; } = true;
    public bool UseDistributedLock { get; init; }
    public TimeSpan LockExpiry { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan LockWaitTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan? NegativeTtl { get; init; }                // cache null result
}

Task<T?> GetOrSetAsync<T>(
    string key,
    Func<CancellationToken, Task<T?>> factory,
    GetOrSetOptions<T> options,
    CancellationToken ct = default);
```

#### Khi nào bật `UseDistributedLock`?
- **Bật**: factory đắt (DB query nặng, gọi external API), TTL ≥ 30s, miss đồng thời từ nhiều process có thể giết DB.
- **Tắt**: TTL ngắn (< 5s — polling pattern), factory rẻ, lock overhead lớn hơn cost của redundant DB hit.

#### Dynamic TTL — `ExpirySelector`
TTL được tính từ kết quả factory. Hữu ích khi cache giá trị có nhiều tier (terminal vs non-terminal, hot vs cold):

```csharp
var options = new GetOrSetOptions<Result<OrderTicketStatusDto>>
{
    ExpirySelector = r => r.IsSuccess && r.Value is { } v && IsTerminal(v.Status)
        ? TimeSpan.FromSeconds(300)    // terminal → cache lâu
        : TimeSpan.FromSeconds(2),     // non-terminal → refresh nhanh
};
```

#### Negative caching — `NegativeTtl`
Khi factory trả `null` (not found) và `NegativeTtl` được set, key sẽ được cache với value JSON `null` trong khoảng thời gian này → các request tiếp theo trả về `null` ngay không gọi DB. Trước khi gọi factory, `GetOrSetAsync` thực hiện thêm 1 `ExistsAsync` để phát hiện negative cache entry.

### Legacy overload
```csharp
Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default);
```
Vẫn hoạt động — forward sang overload mới với `UseSingleFlight = true`. Caller cũ tự động hưởng stampede protection.

### `EvalAsync` — throw-on-failure
Lua atomic là contract; caller (vd rate limiter, flash sale) cần biết để tự fallback. Xem [PlaceOrderRateLimitMiddleware](../../src/Services/Order/UrbanX.Order.API/Middleware/PlaceOrderRateLimitMiddleware.cs) — try/catch + degraded semaphore mode.

---

## Resilience — `RedisCircuitBreaker`

Trạng thái: `Closed` → `Open` → `Probing` → `Closed`.

| Trigger | Action |
|---|---|
| 5 lỗi liên tiếp | Mở circuit, skip Redis trong 30s |
| Cooldown hết hạn | Vào probe — 1 request thử Redis |
| Probe success | Đóng circuit, reset counter |
| Probe fail | Re-open với cooldown mới |

`RedisCircuitBreaker` được dùng chung bởi:
- `RedisCacheService` (Get/Set/Remove/EvalAsync)
- `CacheQueryPipelineBehavior`, `DistributedLockPipelineBehavior`, `IdempotencyPipelineBehavior` (Shared.Messaging)

Một lỗi ở bất kỳ path nào đều ảnh hưởng tất cả → tiết kiệm thêm round-trip khi Redis đã được xác định là down.

---

## Distributed lock attribute

```csharp
[DistributedLock("payment:{PaymentId}", ExpirySeconds = 30, WaitTimeoutSeconds = 15)]
public record ExpirePaymentCommand(Guid PaymentId, ...);
```

Khi circuit open → return `CacheErrors.LockUnavailable` (hard fail) thay vì chạy không có mutual exclusion (an toàn cho mutation).

---

## Caller patterns đã áp dụng

| Caller | Pattern |
|---|---|
| `[CacheQuery]` attribute | Pipeline behavior `CacheQueryPipelineBehavior` — full stampede + L1/L2 |
| `GetOrderByTicketQueryHandler` | `GetOrSetAsync` overload mới với dynamic TTL (terminal 300s / non-terminal 2s) |
| `PlaceOrderRateLimitMiddleware` | `EvalAsync` (Lua) + try/catch fallback `SemaphoreSlim(20)` degraded mode |
| `CouponClaimRedisGateway` | `EvalAsync` (Lua atomic) — throw on fail (an toàn cho tiền) |
| `RedisFlashSaleStockService` | `EvalAsync` reserve (throw); restore best-effort |
| `OrderTerminalStatusCacheConsumer` | `RemoveAsync` — silent fail-and-forget |

---

## Khi viết caller mới — checklist

1. **Read path** (cache-aside) → ưu tiên `[CacheQuery]` attribute. Nếu cần dynamic TTL / logic phức tạp → dùng `GetOrSetAsync` overload mới.
2. **Write path** (cache invalidation) → `RemoveAsync` / `RemoveByPatternAsync` — đã silent fail.
3. **Atomic op** (rate limit, counter, flash sale) → `EvalAsync` Lua + tự xử lý exception (degraded mode hoặc rethrow).
4. **Mutex** trên Command → `[DistributedLock]` attribute.
5. **Idempotency** trên Command → `IIdempotentCommand` interface.
