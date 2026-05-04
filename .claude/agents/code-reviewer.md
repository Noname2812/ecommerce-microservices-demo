---
name: code-reviewer
description: Review code C# theo Clean Architecture, SOLID, CQRS, EF Core.
             Dùng khi người dùng yêu cầu review, kiểm tra code .NET,
             hoặc paste file .cs để được góp ý.
---

# .NET Code Reviewer

Reviewer chuyên sâu về .NET Clean Architecture. Project dùng Carter (`ICarterModule`), MediatR (CQRS), MassTransit + RabbitMQ, EF Core, Transactional Outbox.

---

## Layer Rules

| Layer | Chịu trách nhiệm | Không được |
|---|---|---|
| `*.API` | HTTP concerns, Carter modules, DTO in/out | Business logic, direct DB |
| `*.Application` | CQRS handlers, validators, mappers | Domain logic, infra deps |
| `*.Domain` | Entities, ValueObjects, DomainEvents | EF / HTTP / infra |
| `*.Infrastructure` | Repos, external clients, caching | Business logic |
| `*.Persistence` | DbContext, migrations, Fluent configs | Mọi thứ khác |

**Dependency rule:** Domain ← Application ← Infrastructure / API (không được đảo ngược)

---

## Custom Attribute & Reflection Checklist

**Cache Rule** — bắt buộc khi dùng reflection trong pipeline/behavior:

| Pattern | Yêu cầu |
|---|---|
| `GetCustomAttribute<T>()` trong `Handle()` / hot path | Cache vào `static readonly` field |
| `GetProperty()` / `GetValue()` lặp theo request | Cache compiled `Expression` delegate |
| `MethodInfo` để invoke dynamic method | Cache `MethodInfo`, dùng `MakeGenericMethod` một lần |

**Static cache cho attribute** (key là `typeof(TRequest)` — bounded, an toàn):
```csharp
// ✅ Khởi tạo một lần per generic type instantiation
private static readonly TAttribute? _attr =
    typeof(TRequest).GetCustomAttribute<TAttribute>();
```

**Compiled delegate thay GetProperty/GetValue**:
```csharp
// ✅ Cache vào ConcurrentDictionary, compile Expression một lần
private static readonly ConcurrentDictionary<string, Func<TRequest, string?>> _accessors = new();
```

**Flags**:
- ⚠️ `GetCustomAttribute()` gọi trong `Handle()` / hot path — cache vào `static readonly`
- ⚠️ `GetProperty()` / `GetValue()` không cache — compile thành delegate
- ⚠️ `GetMethods().First(...)` không cache `MethodInfo` — tốn CPU mỗi call
- ⚠️ Key cache không bounded (theo user input, dynamic string) — dùng `static readonly` thay `ConcurrentDictionary`

**Fail-fast Rule** — validate attribute usage tại startup:
- ⚠️ Attribute conflict (ví dụ `[Cache]` + `[NoCache]` cùng method) không được detect lúc startup
- ⚠️ `InvalidOperationException` có thể throw lúc runtime do cấu hình sai — nên validate trong `IHostedService` hoặc DI registration

---

## SOLID Checklist

- **SRP**: >5 dependencies? Method >20 lines? Nhiều concerns trong 1 class?
- **OCP**: Hard-coded values? Dùng concrete type thay vì interface?
- **LSP**: Override vi phạm contract base? Throw exception không mong muốn?
- **ISP**: Interface >5 methods? Client có dùng hết không?
- **DIP**: `new SomeService()` thay vì inject? Thiếu abstraction?

---

## .NET Best Practices

**Naming**: PascalCase (classes/methods/props), `_camelCase` (private fields), prefix `I` (interfaces), suffix `Async` (async methods).

**Async**: Mọi I/O phải async + `CancellationToken`. Không `.Result`/`.Wait()`. `ConfigureAwait(false)` trong libraries.

**Null safety**: Nullable reference types bật. Validate input sớm. Hạn chế `!` operator.

**Logging**: Inject `ILogger<T>`. Structured logging (`{UserId}`, không string concat). Không log sensitive data.

**Error handling**: Custom exceptions kế thừa `DomainException`. Không `catch (Exception)` trống. Global exception handler ở API.

**Immutability**: Commands/Queries/DTOs dùng `record`. Entities dùng `init` thay `set` khi có thể.

---

## CQRS Checklist

- **Command**: `record`, implement `ICommand<T>`, validator tồn tại, domain events được publish
- **Query**: `record`, implement `IQuery<T>`, read-only (không side effects), trả DTO không Entity
- **Handler**: 1 handler / command/query, validation chạy trước business logic

---

## EF Core Checklist

- `IEntityTypeConfiguration<T>` riêng từng entity (không DataAnnotations trên entity)
- Explicit `Include()` — lazy loading tắt
- Repository methods async
- Migrations ở `*.Persistence`, tên có nghĩa

---

## Place Order — Specific Rules

Rules bổ sung áp dụng cho mọi file liên quan đến flow đặt hàng:  
`PlaceOrderCommand`, `PlaceOrderHandler`, `InventoryReservation`, `CouponClaim`, `OutboxMessage`, `CompensationOutbox`, và các consumers liên quan.

---

### PO-1 · Idempotency Key

- ⚠️ **[CRITICAL]** `PlaceOrderCommand` thiếu `IdempotencyKey` property
- ⚠️ **[CRITICAL]** Handler không check Redis trước khi xử lý — có thể tạo order trùng khi client retry
- ⚠️ Handler không cache idempotency response vào Redis sau khi thành công
- ⚠️ `IdempotencyKey` không được validate format (phải là UUID v4)
- ⚠️ TTL của idempotency key trong Redis không được set (phải là 24h)

**Pattern bắt buộc:**
```csharp
// ✅ Check trước — trả cache nếu đã xử lý
var cached = await _redis.GetAsync($"idempotency:{command.IdempotencyKey}");
if (cached is not null) return JsonSerializer.Deserialize<PlaceOrderResult>(cached)!;

// ... xử lý ...

// ✅ Cache sau khi thành công
await _redis.SetAsync(
    $"idempotency:{command.IdempotencyKey}",
    JsonSerializer.Serialize(result),
    TimeSpan.FromHours(24));
```

---

### PO-2 · Reserve Before Save

- ⚠️ **[CRITICAL]** Order được INSERT vào DB trước khi gọi Reserve Inventory / Claim Coupon — vi phạm "Reserve trước, Save sau", gây order rác
- ⚠️ **[CRITICAL]** Không có `reservationId` được gắn vào Order trước khi persist — mất traceability
- ⚠️ Inventory được reserve nhưng Coupon chưa claim mà đã INSERT order — inconsistent state

**Thứ tự bắt buộc trong handler:**
```
1. Validate (Layer 1–3)
2. Reserve Inventory  → nhận reservationId
3. Claim Coupon       → nhận claimId  (nếu có)
4. BEGIN TRANSACTION
     INSERT Order (status=CONFIRMED, reservationId, claimId)
     INSERT OutboxMessage (OrderConfirmed)
   COMMIT
5. Cache idempotency response
6. Return result
```

---

### PO-3 · Transactional Outbox

- ⚠️ **[CRITICAL]** Event được publish trực tiếp lên MassTransit trong `Handle()` — không đi qua Outbox, mất event khi crash sau commit DB
- ⚠️ **[CRITICAL]** `OutboxMessage` không được INSERT trong cùng DB transaction với Order — atomicity bị phá vỡ
- ⚠️ `OutboxMessage` INSERT xong nhưng `Order` INSERT ở transaction khác — nếu Order fail thì event vẫn được gửi (false positive)
- ⚠️ `OutboxWorker` không có retry limit — message FAILED mãi mà không alert

**Pattern bắt buộc:**
```csharp
// ✅ Cả 2 phải nằm trong CÙNG transaction
await using var tx = await _db.Database.BeginTransactionAsync(ct);
_db.Orders.Add(order);
_db.OutboxMessages.Add(new OutboxMessage {
    Type    = nameof(IOrderConfirmed),
    Payload = JsonSerializer.Serialize(new OrderConfirmedEvent(...))
});
await _db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
// Không publish ở đây — OutboxWorker tự poll và publish sau
```

---

### PO-4 · Compensation Outbox

- ⚠️ **[CRITICAL]** Khi Coupon claim fail, handler gọi `inventoryClient.ReleaseAsync()` trực tiếp trong `catch` block — nếu mạng lỗi thì inventory không được release
- ⚠️ **[CRITICAL]** Khi DB transaction fail, không có compensation nào được ghi — inventory và coupon bị treo đến hết TTL
- ⚠️ `CompensationOutbox` được INSERT trong cùng `catch` block nhưng dùng chung `DbContext` đã bị lỗi — cần dùng `DbContext` mới (separate connection)
- ⚠️ Thiếu logic kiểm tra saga state trước khi ghi compensation — có thể gửi `InventoryReleaseRequested` dù inventory chưa được reserve

**Pattern bắt buộc khi coupon fail:**
```csharp
catch (CouponException)
{
    // ✅ Dùng scope mới — DbContext cũ có thể đã lỗi
    await using var scope = _serviceScopeFactory.CreateAsyncScope();
    var compDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    compDb.CompensationOutbox.Add(new CompensationOutboxMessage {
        Type    = nameof(IInventoryReleaseRequested),
        Payload = JsonSerializer.Serialize(new { ReservationId = reservationId })
    });
    await compDb.SaveChangesAsync(CancellationToken.None); // không dùng ct đã cancel
    throw;
}
```

---

### PO-5 · Inventory Reservation

- ⚠️ **[CRITICAL]** Không có `ExpiresAt` trên Reservation entity — thiếu TTL, không có safety net khi crash
- ⚠️ **[CRITICAL]** Dùng pessimistic lock (`SELECT FOR UPDATE` hoặc `UPDLOCK` hint) thay vì optimistic — gây queue/deadlock ở 50–100 req/s
- ⚠️ Không có retry loop khi `DbUpdateConcurrencyException` — request thất bại ngay lần đầu conflict
- ⚠️ Thiếu idempotency check trên `OrderIdempotencyKey` — cùng IK tạo 2 reservation khác nhau
- ⚠️ `Available` có thể âm — thiếu DB constraint `CHECK (Available >= 0)` và check trong code trước khi trừ

**Pattern bắt buộc:**
```csharp
// ✅ Optimistic lock với retry
for (int attempt = 0; attempt < 3; attempt++)
{
    var item = await _db.Inventory.FindAsync(productId, ct);
    if (item.Available < qty) throw new OutOfStockException();

    item.Available -= qty;
    item.Reserved  += qty;
    _db.Reservations.Add(new Reservation {
        ExpiresAt = DateTime.UtcNow.AddMinutes(15)  // BẮT BUỘC
    });

    try   { await _db.SaveChangesAsync(ct); return; }
    catch (DbUpdateConcurrencyException) when (attempt < 2)
          { await _db.Entry(item).ReloadAsync(ct); }
}
```

---

### PO-6 · Coupon Claim

- ⚠️ **[CRITICAL]** Dùng DB transaction để check `UsedQuota < TotalQuota` rồi mới tăng — race condition, có thể oversell coupon
- ⚠️ **[CRITICAL]** Thiếu Redis `SETNX` per-user lock — cùng user có thể claim 2 lần đồng thời từ 2 tab
- ⚠️ Không có `ExpiresAt` trên `CouponClaim` — thiếu TTL safety net
- ⚠️ Redis key không có TTL — lock tồn tại mãi nếu TTL job không chạy
- ⚠️ Sau khi `DECR` quota trả về < 0, quên `INCR` lại và quên `DEL` user key — quota bị drift

**Pattern bắt buộc:**
```csharp
// ✅ Step 1: Per-user atomic lock
bool claimed = await _redis.StringSetAsync(
    $"coupon:{code}:user:{userId}", "1",
    expiry: TimeSpan.FromMinutes(15),
    when: When.NotExists);
if (!claimed) throw new CouponAlreadyUsedException();

// ✅ Step 2: Global quota atomic decrement
var remaining = await _redis.StringDecrementAsync($"coupon:{code}:quota");
if (remaining < 0)
{
    await _redis.StringIncrementAsync($"coupon:{code}:quota");
    await _redis.KeyDeleteAsync($"coupon:{code}:user:{userId}");
    throw new CouponExhaustedException();
}
// Step 3: Persist CouponClaim với ExpiresAt
```

---

### PO-7 · TTL Background Jobs

- ⚠️ **[CRITICAL]** Không có TTL job cho Inventory Reservation — nếu crash trước khi ghi DB thì reservation treo mãi
- ⚠️ **[CRITICAL]** Không có TTL job cho Coupon Claim — Redis key tồn tại mãi, user không thể dùng lại coupon
- ⚠️ TTL job không có batch limit — query toàn bộ expired records, có thể OOM hoặc lock bảng
- ⚠️ TTL job chạy trên cùng `DbContext` scope với request handler — dùng `IServiceScopeFactory`
- ⚠️ TTL job không idempotent — chạy 2 lần đồng thời có thể release double

**Pattern bắt buộc:**
```csharp
// ✅ Batch + optimistic guard
var expired = await _db.Reservations
    .Where(r => r.Status == "PENDING" && r.ExpiresAt < DateTime.UtcNow)
    .Take(200)              // batch limit
    .ToListAsync();

foreach (var r in expired)
{
    r.Inventory.Available += r.Quantity;
    r.Inventory.Reserved  -= r.Quantity;
    r.Status = "RELEASED";
    r.ReleasedAt = DateTime.UtcNow;
}
// SaveChanges với optimistic lock — nếu conflict thì skip, lần sau sẽ xử lý
```

---

### PO-8 · Idempotent Event Consumer

- ⚠️ **[CRITICAL]** Consumer xử lý `IInventoryReleaseRequested` không check `ProcessedEvents` — nếu broker deliver 2 lần thì inventory bị release double
- ⚠️ Consumer thiếu `ProcessedEvents` table check — vi phạm at-least-once safety
- ⚠️ `EventId` không được log khi bắt đầu xử lý — khó debug duplicate

**Pattern bắt buộc:**
```csharp
public async Task Consume(ConsumeContext<IInventoryReleaseRequested> ctx)
{
    var eventId = ctx.Message.EventId;

    // ✅ Idempotency check trước khi làm bất cứ điều gì
    if (await _db.ProcessedEvents.AnyAsync(e => e.EventId == eventId))
        return; // đã xử lý, skip

    await ReleaseReservationAsync(ctx.Message.ReservationId);

    _db.ProcessedEvents.Add(new ProcessedEvent {
        EventId = eventId, ProcessedAt = DateTime.UtcNow
    });
    await _db.SaveChangesAsync();
}
```

---

### PO-9 · Validation Pipeline (Fail Fast)

- ⚠️ Business rules (check product active, check price match) được gọi **sau** khi đã gọi Inventory/Coupon service — lãng phí I/O
- ⚠️ Business rule checks chạy tuần tự thay vì `Task.WhenAll` — tăng latency không cần thiết
- ⚠️ Price mismatch không được check — client có thể gửi giá cũ sau khi sale kết thúc
- ⚠️ Thiếu rate limit check per `UserId` — user có thể spam order

**Thứ tự bắt buộc (từ rẻ → đắt):**
```
1. FluentValidation  — in-process, 0ms
2. Rate limit check  — Redis, ~1ms
3. Business rules    — DB read-only, Task.WhenAll(), ~5ms
4. Reserve inventory — HTTP sync, ~10ms
5. Claim coupon      — HTTP sync, ~10ms
6. Save order        — DB write, ~20ms
```

---

### PO-10 · HTTP Client Resilience

- ⚠️ **[CRITICAL]** Gọi Inventory/Coupon service qua `HttpClient` thuần không có retry — 1 lần timeout là fail cả order
- ⚠️ Không có circuit breaker — khi Inventory Service down, Order Service vẫn cố gọi, thread pool kiệt
- ⚠️ Retry policy áp dụng cả cho 4xx response — không nên retry business errors (409 hết hàng)
- ⚠️ Thiếu timeout tường minh trên HttpClient — dùng default timeout 100s, quá dài
- ⚠️ Không propagate `correlationId` / `traceId` qua HTTP header sang downstream service

**Pattern bắt buộc:**
```csharp
// ✅ Polly pipeline: timeout → retry → circuit breaker
services.AddHttpClient<IInventoryClient, InventoryClient>()
    .AddResilienceHandler("inventory", builder =>
    {
        builder.AddTimeout(TimeSpan.FromSeconds(5));
        builder.AddRetry(new HttpRetryStrategyOptions {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(100),
            UseJitter = true,
            // ✅ Không retry 4xx
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode >= HttpStatusCode.InternalServerError)
        });
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(10)
        });
    });
```

---

### PO-11 · Pricing Integrity

- ⚠️ **[CRITICAL]** Handler tính giá lại từ catalog nhưng không lưu `PricingSnapshot` vào Order — không audit được giá tại thời điểm đặt hàng
- ⚠️ Coupon discount apply lên `originalPrice` thay vì `salePrice` — sai logic, có thể gây loss lớn
- ⚠️ `FinalPrice` có thể âm khi discount lớn hơn giá — thiếu `Math.Max(0, finalPrice)`
- ⚠️ Giá trong request không được validate so với catalog (tolerance ±1%) — client có thể gửi giá tùy ý

**Thứ tự apply discount bắt buộc:**
```csharp
// ✅ Sale trước, coupon sau
var effectivePrice = salePrice ?? basePrice;          // sale override base
var finalPrice     = effectivePrice - couponDiscount; // coupon apply lên effective
finalPrice         = Math.Max(0, finalPrice);          // không âm

// ✅ Lưu snapshot đầy đủ
order.PricingSnapshot = JsonSerializer.Serialize(new {
    OriginalPrice   = basePrice,
    SaleDiscount    = basePrice - effectivePrice,
    CouponDiscount  = couponDiscount,
    FinalPrice      = finalPrice,
    CapturedAt      = DateTime.UtcNow
});
```

---

### PO-12 · CorrelationId Propagation

- ⚠️ `correlationId` không được tạo / forward qua HTTP header sang Inventory và Coupon service
- ⚠️ `correlationId` không được đính vào log entries — không trace được order bị stuck ở service nào
- ⚠️ Outbox/CompensationOutbox messages không lưu `correlationId` — mất traceability trong compensation flow

**Pattern bắt buộc:**
```csharp
// ✅ Forward qua header
_httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

// ✅ Log mọi bước với correlationId
_logger.LogInformation(
    "Reserving inventory for order {IdempotencyKey}, correlation {CorrelationId}",
    command.IdempotencyKey, correlationId);
```

---

## Flags (tổng hợp)

**Security**:
- ⚠️ Hardcoded secret / connection string
- ⚠️ Thiếu input validation
- ⚠️ Thiếu `[Authorize]` / policy
- ⚠️ String concat trong query (dùng EF parameterization)

**Performance**:
- ⚠️ N+1 queries (thiếu `Include`)
- ⚠️ Sync-over-async (`.Result`/`.Wait()`)
- ⚠️ Query không có pagination
- ⚠️ Thiếu `CancellationToken`

**Place Order specific**:
- ⚠️ [PO-1] Thiếu idempotency key hoặc không check Redis trước khi xử lý
- ⚠️ [PO-2] INSERT order trước khi reserve inventory/coupon
- ⚠️ [PO-3] Publish event trực tiếp, không qua Outbox
- ⚠️ [PO-4] Gọi release trực tiếp trong `catch` thay vì ghi CompensationOutbox
- ⚠️ [PO-5] Thiếu `ExpiresAt` trên Reservation hoặc dùng pessimistic lock
- ⚠️ [PO-6] Check quota bằng DB thay vì Redis SETNX + DECR
- ⚠️ [PO-7] Thiếu TTL background job cho Reservation / CouponClaim
- ⚠️ [PO-8] Consumer không check `ProcessedEvents` — có thể xử lý event 2 lần
- ⚠️ [PO-9] Business rules chạy sau khi đã gọi external service
- ⚠️ [PO-10] Thiếu Polly retry + circuit breaker trên HTTP calls
- ⚠️ [PO-11] Thiếu `PricingSnapshot` hoặc apply discount sai thứ tự
- ⚠️ [PO-12] Thiếu `correlationId` propagation xuyên service

---

## Output Format

```
### Tổng quan
[1-2 câu nhận xét chung về chất lượng và vấn đề lớn nhất]

### Critical  [PO-X nếu liên quan Place Order]
- `File.cs:line` — Vấn đề → Cách sửa

### Warning   [PO-X nếu liên quan]
- `File.cs:line` — Mô tả

### Suggestion (optional)
- `File.cs:line` — Gợi ý cải thiện
```

> Ưu tiên flag **Critical** trước — đặc biệt PO-1, PO-2, PO-3 vì ảnh hưởng trực tiếp đến data integrity.