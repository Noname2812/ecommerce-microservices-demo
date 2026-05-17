# TASK-11 — Replace Shared.Outbox với MassTransit EF Outbox

**Team:** Order · **Effort:** M (1.5d) · **Depends:** TASK-03
**Branch:** `feature/order-refactor/TASK-11-mt-ef-outbox`

## Mục đích

Bỏ custom `Shared.Outbox` ở **Order service only**. Dùng MassTransit built-in `AddEntityFrameworkOutbox<TDbContext>()` thay 1-1: atomic-with-SaveChanges, at-least-once, MT tự register `BusOutboxDeliveryService` IHostedService.

⚠ **CHỈ Order service**. Catalog/Identity/Payment giữ `Shared.Outbox` không touch.

## Files MODIFY

### `Order.Persistence/OrderDbContext.cs`
```csharp
// Before
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options)
    : OutboxDbContext(options)

// After
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options)
    : DbContext(options)
```

Bỏ `using Shared.Outbox.EfCore;`.

Override `OnModelCreating` cho MT outbox entities (nếu `UseSnakeCaseNamingConvention()` không tự áp dụng cho MT internal types):
```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);

    // Force snake_case cho MT outbox tables (nếu cần)
    builder.Entity<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
        .ToTable("inbox_state");
    builder.Entity<MassTransit.EntityFrameworkCoreIntegration.OutboxState>()
        .ToTable("outbox_state");
    builder.Entity<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
        .ToTable("outbox_message");
}
```
**TEST trước**: chạy `dotnet ef migrations add` xem MT tables tự `inbox_state` hay `InboxState`. Nếu tự snake_case → bỏ override này.

### `Order.API/Program.cs`

**Remove:**
```csharp
builder.Services.AddOutbox<OrderDbContext>(configureDb: null, builder.Configuration);
builder.Services.AddCompensationOutbox(builder.Configuration);
```

**Thêm vào trong `AddMessaging(builder.Configuration, configureBus: bus => { ... })`:**
```csharp
bus.AddEntityFrameworkOutbox<OrderDbContext>(o =>
{
    o.UsePostgres();
    o.UseBusOutbox();                           // bật bus outbox (publish qua IPublishEndpoint stage vào outbox)
    o.QueryDelay              = TimeSpan.FromSeconds(1);
    o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
});
```

⚠ Gọi `bus.AddEntityFrameworkOutbox`, **không phải** `services.AddEntityFrameworkOutbox` (sai signature). MT tự register `BusOutboxDeliveryService` IHostedService.

Bỏ `using Shared.Outbox.DependencyInjection.Extensions;`.

### `Order.Application/Usecases/V1/Command/CancelOrder/CancelOrderCommandHandler.cs`

Đã được rewrite ở TASK-06 (PlaceOrder/PlaceSalesOrder). CancelOrder cần làm tương tự:

```csharp
public sealed class CancelOrderCommandHandler(
    IOrderRepository orderRepository,
    IPublishEndpoint publishEndpoint,
    IUserContext userContext)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order is null)
            return Result.Failure(OrderErrors.NotFound(request.OrderId));

        var userId  = userContext.UserId!.Value;
        var isAdmin = userContext.HasRole(Roles.Admin);

        if (!isAdmin && !order.CanBeCancelledBy(userId))
            return Result.Failure(OrderErrors.Forbidden);

        if (order.Status == OrderStatus.Cancelled
            || order.Status == OrderStatus.Completed
            || order.Status == OrderStatus.Refunded)
            return Result.Failure(OrderErrors.CannotCancel);

        var hadReservation = order.ReservationId.HasValue;
        var hadCoupon      = order.CouponClaimId.HasValue;
        var reservationId  = order.ReservationId;
        var claimId        = order.CouponClaimId;

        order.Cancel(request.Reason, userId, "");

        // Publish events — MT EF Outbox stage atomic với SaveChanges
        await publishEndpoint.Publish(
            new OrderIntegrationEvents.OrderCancelledV1(order.Id, order.OrderNumber, request.Reason),
            ctx => ctx.MessageId = DeterministicGuid($"order-cancelled:{order.Id}"),
            ct);

        if (hadReservation)
        {
            await publishEndpoint.Publish(
                new InventoryReleaseRequestedV1
                {
                    OrderId       = order.Id,
                    ReservationId = reservationId!.Value,
                    Reason        = $"Cancelled by user: {request.Reason}"
                },
                ctx => ctx.MessageId = DeterministicGuid($"inv-release:{reservationId}"),
                ct);
        }

        if (hadCoupon)
        {
            await publishEndpoint.Publish(
                new CouponReleaseRequestedV1
                {
                    OrderId = order.Id,
                    ClaimId = claimId!.Value,
                    Reason  = $"Cancelled by user: {request.Reason}"
                },
                ctx => ctx.MessageId = DeterministicGuid($"coupon-release:{claimId}"),
                ct);
        }

        return Result.Success();
    }

    private static Guid DeterministicGuid(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes.Take(16).ToArray());
    }
}
```

### .csproj — bỏ Shared.Outbox

- `Order.Application.csproj` — bỏ `<ProjectReference Include="..\..\..\Shared\Shared.Outbox\Shared.Outbox.csproj" />`
- `Order.Persistence.csproj` — bỏ tương tự
- `Order.API.csproj` — bỏ tương tự

### Cleanup `using` statements

Grep và bỏ:
- `using Shared.Outbox.Abstractions;`
- `using Shared.Outbox.EfCore;`
- `using Shared.Outbox.DependencyInjection.Extensions;`
- `using Shared.Outbox.DependencyInjection.Options;`

Trong tất cả file Order service.

### Verify `Directory.Packages.props`

`MassTransit.EntityFrameworkCore` đã có sẵn (vì saga state EF repository đã dùng). Verify version compatible với MT EF Outbox (8.x).

## Acceptance Criteria

- [ ] Build OK — không còn reference Shared.Outbox trong Order service
- [ ] `grep` `Shared.Outbox` trong `src/Services/Order/**/*.cs` returns empty (trừ migration cũ trong `Migrations/`)
- [ ] Migration generate (TASK-12) sẽ:
  - DROP `outbox_messages`, `outbox_processed_events`, `compensation_outbox_messages`
  - ADD `inbox_state`, `outbox_state`, `outbox_message`
- [ ] Integration test:
  - PlaceOrder → check Postgres `outbox_message` có row tạm thời (publishing), purge sau ~1-2s
  - Aspire RabbitMQ: event publish đúng, không duplicate
  - PaymentSessionCompleted xử lý: Order CONFIRMED + Inventory deduct + OrderConfirmedV1 publish (tất cả atomic)

## Notes

- **MT EF Outbox vs Shared.Outbox**: Tương đương về at-least-once guarantee, nhưng MT tích hợp sâu hơn (auto `MessageId` dedup, Inbox table dedupe consumer, không cần custom worker).
- **DuplicateDetectionWindow=10min**: nếu cùng MessageId trong 10p → MT skip publish thứ 2. Đủ cho saga retry sau OPCC.
- **Saga interaction**: Saga state EF repository (`EntityFrameworkRepository`) đã có; MT Outbox **không** conflict với saga repository. Cả 2 dùng cùng `OrderDbContext`.
- **TransactionPipelineBehavior** (Shared.Messaging) wrap handler trong `ExecuteInTransactionAsync` — verify MT Outbox publish stage được vào TX đó. Nếu UoW dùng explicit BeginTransaction → MT Outbox cần enlist; `UseBusOutbox()` đảm bảo MT enlist vào ambient TX.

## DoD

- [ ] Build pass
- [ ] PR merge
- [ ] Unblock TASK-12
