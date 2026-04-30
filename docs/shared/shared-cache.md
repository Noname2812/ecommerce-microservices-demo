# Shared.Cache

Redis-backed distributed cache, distributed lock, và Lua script execution cho toàn hệ thống UrbanX.

## Mục đích

- Cung cấp một abstraction cache nhất quán cho tất cả services
- Distributed lock an toàn trên Redis Cluster (SET NX PX — không dùng KEYS)
- Lua script execution trực tiếp qua `IDatabase`
- Fix `IdempotencyPipelineBehavior` — register `IDistributedCache` Redis thay vì in-memory

## DI Registration

Trong `Program.cs` của service:

```csharp
builder.AddSharedCache("redis");  // trước AddApplication()
```

Trong `AppHost.cs`:

```csharp
builder.AddProject<Projects.UrbanX_Catalog_API>("catalog")
    .WithReference(redis)
    .WaitFor(redis)
    // ...
```

## ICacheService

```csharp
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveByPatternAsync(string pattern, CancellationToken ct = default);  // SCAN-based
    Task<RedisResult> EvalAsync(string script, RedisKey[] keys, RedisValue[]? args = null, CancellationToken ct = default);
    Task<RedisResult> EvalAsync(LuaScript preparedScript, object? parameters = null, CancellationToken ct = default);
}
```

**Key tự động được prefix:** `{InstanceName}:{key}` (ví dụ: `urbanx:catalog:product:abc123`).

**Serialization:** System.Text.Json — không cần thêm dependency.

### Ví dụ: Cache-aside trong Query Handler

```csharp
public class GetProductByIdQueryHandler(ICacheService cache, IProductRepository repo)
    : IQueryHandler<GetProductByIdQuery, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        var cacheKey = $"catalog:product:{query.ProductId}";
        var product = await cache.GetOrSetAsync(
            cacheKey,
            () => repo.GetByIdAsync(query.ProductId, ct),
            TimeSpan.FromMinutes(5),
            ct);

        return product is null
            ? Result.Failure<ProductDto>(CatalogErrors.ProductNotFound(query.ProductId))
            : Result.Success(product.ToDto());
    }
}
```

### Ví dụ: Invalidate cache sau Command

```csharp
await _cache.RemoveAsync($"catalog:product:{command.ProductId}", ct);

// Hoặc invalidate toàn bộ products của seller:
await _cache.RemoveByPatternAsync($"catalog:product:seller:{sellerId}:*", ct);
```

## IDistributedLockService

```csharp
public interface IDistributedLockService
{
    Task<ILockHandle?> TryAcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default);
    Task<ILockHandle?> AcquireAsync(string resource, TimeSpan expiry, TimeSpan waitTimeout, CancellationToken ct = default);
}

public interface ILockHandle : IAsyncDisposable
{
    bool IsAcquired { get; }
    string Resource { get; }
}
```

**Cơ chế:**
- Acquire: `SET key token NX PX ttl` — atomic, cluster-safe
- Release: Lua script check-and-delete (`if get(key)==token then del(key)`)
- Chỉ owner (holder của token) mới release được lock

### Ví dụ: Inject trực tiếp vào Handler

```csharp
public class ProcessPaymentCommandHandler(IDistributedLockService lockService) : ...
{
    public async Task<Result> Handle(ProcessPaymentCommand command, CancellationToken ct)
    {
        var handle = await lockService.AcquireAsync(
            $"payment:order:{command.OrderId}",
            expiry: TimeSpan.FromSeconds(30),
            waitTimeout: TimeSpan.FromSeconds(10),
            ct);

        if (handle is null)
            return Result.Failure(CacheErrors.LockTimeout($"payment:order:{command.OrderId}"));

        await using (handle)
        {
            // critical section
        }
        return Result.Success();
    }
}
```

## [DistributedLock] Attribute — MediatR Pipeline

Cách thuận tiện hơn: gắn attribute trực tiếp trên Command/Query. `DistributedLockPipelineBehavior` (trong Shared.Messaging) tự động acquire/release.

```csharp
[DistributedLock("checkout:{UserId}", ExpirySeconds = 60, WaitTimeoutSeconds = 10)]
[RequirePermission(Permissions.Orders.Write)]
public record CheckoutCommand(Guid UserId, Guid CartId) : ICommand<Result<Guid>>;
```

**Template `{PropertyName}`:** bất kỳ property public nào của Command/Query đều có thể dùng làm key.

**Khi timeout:** behavior trả `Result.Failure(CacheErrors.LockTimeout(resource))` — không throw exception.

**Thứ tự behaviors:** `Logging → Idempotency → DistributedLock → Authorization → Validation → Handler`

## Lua Script Execution

```csharp
// Raw script
var result = await _cache.EvalAsync(
    "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
    keys: [new RedisKey("my-lock")],
    args: [new RedisValue("my-token")]);

// Pre-compiled script (reuse để tránh re-compile)
private static readonly LuaScript AtomicIncrement = LuaScript.Prepare(
    "local v = redis.call('incr', KEYS[1])\n" +
    "if v > tonumber(ARGV[1]) then redis.call('set', KEYS[1], ARGV[1]) end\n" +
    "return v");

var count = await _cache.EvalAsync(AtomicIncrement, new {
    KEYS = new RedisKey[] { new("rate:user:123") },
    ARGV = new RedisValue[] { 100 }
});
```

## Configuration

```json
{
  "Shared": {
    "Cache": {
      "InstanceName": "urbanx",
      "DefaultExpiry": "01:00:00"
    }
  }
}
```

| Field | Default | Mô tả |
|---|---|---|
| `InstanceName` | `"urbanx"` | Prefix cho tất cả keys và locks |
| `DefaultExpiry` | `01:00:00` | TTL mặc định khi không truyền `expiry` |

## Cluster Readiness

| Feature | Cơ chế | Cluster-safe? |
|---|---|---|
| Cache get/set | `IDatabase.StringGet/Set` | ✅ (routing tự động) |
| Lock acquire | `SET NX PX` | ✅ (single key → single shard) |
| Lock release | Lua check-and-delete | ✅ |
| Pattern delete | `IServer.ScanAsync` trên từng node | ✅ (tránh `KEYS`) |
| Lua eval | `ScriptEvaluateAsync` | ✅ (keys phải cùng slot) |

> **Lưu ý Lua + Cluster:** tất cả KEYS trong một script phải thuộc cùng hash slot. Dùng hash tags `{tag}` nếu cần đảm bảo nhiều keys vào cùng shard.

## Services đã enable

| Service | AppHost | Program.cs |
|---|---|---|
| Catalog | `.WithReference(redis)` | `builder.AddSharedCache("redis")` |
| Identity | `.WithReference(redis)` | `builder.AddSharedCache("redis")` |
| Inventory | `.WithReference(redis)` | `builder.AddSharedCache("redis")` |

Để thêm service mới:
1. Thêm `<ProjectReference Include="...\Shared.Cache\Shared.Cache.csproj" />` vào `.csproj`
2. Thêm `.WithReference(redis).WaitFor(redis)` trong `AppHost.cs`
3. Gọi `builder.AddSharedCache("redis")` trong `Program.cs`
