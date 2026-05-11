# MediatR Pipeline Behaviors — Hướng dẫn sử dụng

Pipeline behaviors chạy tự động theo thứ tự trước/sau mỗi Command hoặc Query handler.
Dev không cần đăng ký thêm gì — chỉ cần gắn attribute đúng chỗ.

**Thứ tự thực thi:**
```
Logging → Authorization → Idempotency → Validation → DistributedLock → Cache → Transaction → Handler
```

---

## 1. Authorization

**Behavior:** `AuthorizationPipelineBehavior`  
**Applies to:** Command + Query

Mọi Command/Query **bắt buộc** phải có một trong ba attribute sau. Nếu thiếu, request bị từ chối với lỗi `AUTH_REQUIRED`.

### `[RequirePermission]`

```csharp
[RequirePermission(Permissions.Products.Write)]
public record CreateProductCommand(...) : ICommand<Guid>;

[RequirePermission(Permissions.Products.Read)]
public record GetProductQuery(Guid Id) : IQuery<ProductDto>;
```

**`MinScope`** — giới hạn phạm vi tối thiểu user cần có (mặc định: `Own`):

| Scope | Ý nghĩa |
|---|---|
| `PermissionScope.Own` | User có permission với scope Own hoặc All |
| `PermissionScope.All` | User phải có scope All (thường dùng cho admin/staff) |

```csharp
// Chỉ admin-level mới lấy được danh sách tất cả orders
[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.All)]
public record ListAllOrdersQuery(...) : IQuery<PageResult<OrderDto>>;

// User thường chỉ cần Own scope để xem orders của mình
[RequirePermission(Permissions.Orders.Read)]
public record GetMyOrdersQuery(...) : IQuery<PageResult<OrderDto>>;
```

**Multiple permissions** (tất cả đều phải pass — AND logic):

```csharp
[RequirePermission(Permissions.Products.Write)]
[RequirePermission(Permissions.Inventory.Write)]
public record PublishProductCommand(...) : ICommand<Guid>;
```

### `[RequireRole]`

```csharp
[RequireRole(Roles.Admin)]
public record DeactivateUserCommand(Guid UserId) : ICommand;
```

Dùng khi cần check role cứng, không phụ thuộc vào permission scope.

### `[AllowAnonymous]`

```csharp
[AllowAnonymous]
public record LoginCommand(string Email, string Password) : ICommand<TokenDto>;

[AllowAnonymous]
public record GetPublicProductQuery(Guid Id) : IQuery<ProductDto>;
```

### Đọc identity trong handler

Inject `IUserContext` khi cần `UserId` để lọc dữ liệu theo owner:

```csharp
internal sealed class GetMyOrdersQueryHandler(
    IOrderRepository repo,
    IUserContext userContext) : IQueryHandler<GetMyOrdersQuery, PageResult<OrderDto>>
{
    public async Task<Result<PageResult<OrderDto>>> Handle(GetMyOrdersQuery query, CancellationToken ct)
    {
        // Lọc theo userId đang đăng nhập
        return await repo.GetByUserAsync(userContext.UserId!.Value, query.Page, ct);
    }
}
```

> **Không inject `IUserContext`** vào handler nếu không cần ownership check — tránh coupling không cần thiết.

---

## 2. Cache Query

**Behavior:** `CacheQueryPipelineBehavior`  
**Applies to:** Query only (không áp dụng cho Command)

### Cách dùng cơ bản

```csharp
[CacheQuery("product:detail:{Id}")]
[RequirePermission(Permissions.Products.Read)]
public record GetProductDetailQuery(Guid Id) : IQuery<ProductDetailDto>;
```

Template `{PropertyName}` được resolve tự động từ property của Query.

### Tất cả options

```csharp
[CacheQuery(
    "product:list:{CategoryId}:{Page}",
    ExpirySeconds      = 300,   // TTL trên Redis. Mặc định: 300s (5 phút)
    MemoryTtlSeconds   = 5,     // TTL L1 in-process cache. Mặc định: 5s. Set 0 để tắt
    NegativeTtlSeconds = 30,    // Cache kết quả "not found" để tránh hit DB liên tục. Mặc định: 0 (tắt)
    JitterPercent      = 10,    // Jitter ±10% để tránh thundering herd. Mặc định: 10
    LockExpirySeconds  = 10,    // Thời gian giữ lock khi populate cache. Mặc định: 10s
    LockWaitTimeoutSeconds = 5  // Timeout chờ lock. Mặc định: 5s
)]
public record ListProductsByCategoryQuery(Guid CategoryId, int Page) : IQuery<PageResult<ProductDto>>;
```

### Luồng hoạt động

```
L1 hit?  → trả về ngay (không tốn Redis round-trip)
   ↓ miss
Circuit open? → SingleFlight → handler → set L1
   ↓ closed
L2 (Redis) hit? → set L1 → trả về
   ↓ miss
Acquire Redis lock (stampede prevention)
   ↓
Double-check Redis (ai đó đã populate trong lúc chờ lock?)
   ↓ vẫn miss
Handler → set L2 + L1 → trả về
```

### Negative caching — tránh cache stampede cho "not found"

```csharp
// Không bật → mỗi request "product không tồn tại" đều hit DB
[CacheQuery("product:detail:{Id}")]

// Bật → kết quả Failure cũng được cache 60s, DB chỉ bị hit 1 lần/60s
[CacheQuery("product:detail:{Id}", NegativeTtlSeconds = 60)]
```

### Cache invalidation

Dùng `ICacheService.RemoveAsync` hoặc `RemoveByPatternAsync` trong Command handler:

```csharp
internal sealed class UpdateProductCommandHandler(
    IProductRepository repo,
    ICacheService cache) : ICommandHandler<UpdateProductCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UpdateProductCommand cmd, CancellationToken ct)
    {
        // ... update logic ...

        // Xóa cache sau khi update
        await cache.RemoveAsync($"product:detail:{cmd.Id}", ct);

        return Result.Success(product.Id);
    }
}
```

### Khi Redis down

- Tự động fail-open: request vẫn xử lý được, chỉ không có cache
- In-process **SingleFlight** bảo vệ DB: nhiều request đồng thời cùng key → chỉ 1 request gọi handler, các request còn lại chờ kết quả
- **Circuit breaker** tự động skip Redis sau 5 lỗi liên tiếp, probe lại sau 30s

---

## 3. Distributed Lock

**Behavior:** `DistributedLockPipelineBehavior`  
**Applies to:** Command + Query (thực tế chỉ dùng cho Command)

Dùng khi một operation cần đảm bảo **không chạy đồng thời** trên cùng một resource — ví dụ: checkout, trừ tồn kho, nạp tiền.

### Cách dùng

```csharp
[DistributedLock("order:checkout:{UserId}")]
[RequirePermission(Permissions.Orders.Write)]
public record PlaceOrderCommand(Guid UserId, ...) : ICommand<Guid>;
```

### Tất cả options

```csharp
[DistributedLock(
    "inventory:reserve:{ProductId}",
    ExpirySeconds      = 30,  // Lock tự release sau 30s nếu process crash. Mặc định: 30s
    WaitTimeoutSeconds = 5    // Chờ tối đa 5s để acquire lock. Mặc định: 5s
)]
public record ReserveInventoryCommand(Guid ProductId, int Qty) : ICommand;
```

### Khi không acquire được lock

- **Lock timeout** (Redis UP, có process khác đang giữ lock): trả về `Result.Failure(Cache.LockTimeout)` — HTTP 400
- **Redis down** (circuit open hoặc exception): trả về `Result.Failure(Cache.LockUnavailable)` — **không** fallback, fail hard để bảo vệ data integrity

> **Tại sao fail hard khi Redis down?**  
> Lock tồn tại để ngăn race condition. Skip lock = không có mutual exclusion = nguy cơ corrupt data.  
> Tốt hơn là trả lỗi 400/503 cho client retry sau, còn hơn chạy mà không có bảo vệ.

### Scope của lock

Template `{PropertyName}` resolve từ property của Command/Query:

```csharp
// Lock per user — 1 user chỉ checkout 1 lần tại 1 thời điểm
[DistributedLock("checkout:{UserId}")]

// Lock per product — tránh oversell trên cùng 1 sản phẩm
[DistributedLock("inventory:{ProductId}")]

// Lock per cặp user+product
[DistributedLock("cart:add:{UserId}:{ProductId}")]
```

---

## 4. Idempotency

**Behavior:** `IdempotencyPipelineBehavior`  
**Applies to:** Command only

Dùng khi client có thể gửi lại request (retry sau timeout, network flicker) và bạn cần đảm bảo handler chỉ **thực sự chạy một lần** dù nhận nhiều request giống nhau.

### Cách dùng

Command phải implement `IIdempotentCommand`:

```csharp
[RequirePermission(Permissions.Orders.Write)]
public record PlaceOrderCommand(
    Guid UserId,
    List<OrderItemDto> Items,
    string IdempotencyKey,          // ← client tự generate (VD: UUID)
    TimeSpan? IdempotencyTtl = null // ← null = dùng default 24h
) : ICommand<Guid>, IIdempotentCommand;
```

Client truyền `IdempotencyKey` qua request body (hoặc header — tuỳ thiết kế API của bạn).

### Cơ chế hoạt động

1. Request đến → kiểm tra Redis có key `idempotency:{CommandName}:{IdempotencyKey}` chưa
2. **Có** → trả về cached response ngay, không chạy handler
3. **Chưa có** → acquire lock → double-check → chạy handler → lưu response vào Redis → trả về

### TTL

```csharp
// Dùng default TTL (24 giờ)
public TimeSpan? IdempotencyTtl => null;

// Override TTL cho command cụ thể (ví dụ: payment chỉ cần 1 giờ)
public TimeSpan? IdempotencyTtl => TimeSpan.FromHours(1);
```

### Khi Redis down

Fail-open: handler vẫn chạy, chỉ mất khả năng deduplicate. Log warning được emit để tracking.  
Khác với `[DistributedLock]`, đây là an toàn vì command đã idempotent by design — chạy 2 lần không corrupt data.

### Lưu ý

- `IdempotencyKey` phải do **client generate** (UUID/ULID), không phải server
- Không dùng cho Query — behavior chỉ active khi `TResponse != Unit`, Query trả về data nên không phù hợp
- Nếu schema của response thay đổi giữa các deploy, cached entry cũ sẽ bị discard và handler re-execute (safe fallback)

---

## Cheat sheet — Attribute nào cho loại nào?

| Loại operation | Attribute bắt buộc | Attribute nên có |
|---|---|---|
| Query công khai | `[AllowAnonymous]` | `[CacheQuery]` |
| Query cần login | `[RequirePermission(..., Read)]` | `[CacheQuery]` |
| Command thường | `[RequirePermission(..., Write)]` | — |
| Command có race condition | `[RequirePermission]` | `[DistributedLock]` |
| Command client retry | `[RequirePermission]` | implement `IIdempotentCommand` |
| Command vừa lock vừa idempotent | `[RequirePermission]` | `[DistributedLock]` + `IIdempotentCommand` |

---

## Ví dụ tổng hợp — PlaceOrder

```csharp
// Command: cần permission, lock chống oversell, idempotent chống double-submit
[RequirePermission(Permissions.Orders.Write)]
[DistributedLock("checkout:{UserId}", WaitTimeoutSeconds = 10)]
public record PlaceOrderCommand(
    Guid UserId,
    List<OrderItemDto> Items,
    string IdempotencyKey,
    TimeSpan? IdempotencyTtl = null
) : ICommand<Guid>, IIdempotentCommand;

// Query: cache danh sách order, chỉ owner mới xem được
[RequirePermission(Permissions.Orders.Read, MinScope = PermissionScope.Own)]
[CacheQuery("orders:user:{UserId}:{Page}", ExpirySeconds = 60, NegativeTtlSeconds = 10)]
public record GetMyOrdersQuery(Guid UserId, int Page) : IQuery<PageResult<OrderSummaryDto>>;
```
