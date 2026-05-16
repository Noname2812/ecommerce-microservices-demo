# TASK-02 · Saga State + State Machine + Migration

| | |
|---|---|
| **Effort** | ~2 ngày |
| **Depends on** | TASK-01 |
| **Blocks** | TASK-05 |
| **Branch** | `feat/saga/task-02-state-machine` |

## Goal

Implement `PlaceSalesOrderSagaStateMachine` + EF Core persistence cho saga state. Đây là **central orchestrator** điều phối Promotion → Inventory → Coupon → Confirm flow.

## Context

Order service đã có infrastructure sẵn (`SagaStateBase`, `SagaStateMachineBase<T>`, `SagaStateEfCoreConfiguration<T>`) trong `Shared.Messaging/Saga/`. Task này extend các base class đó cho PlaceSalesOrder use case.

OrderDbContext đã inherit `OutboxDbContext` — chỉ cần thêm `DbSet<PlaceSalesOrderSagaState>` + 1 migration.

## Files

### New

1. `src/Services/Order/UrbanX.Order.Domain/Sagas/PlaceSalesOrderSagaState.cs`
2. `src/Services/Order/UrbanX.Order.Application/Sagas/PlaceSalesOrderSagaStateMachine.cs`
3. `src/Services/Order/UrbanX.Order.Persistence/Configurations/PlaceSalesOrderSagaStateConfiguration.cs`
4. EF migration: `AddPlaceSalesOrderSagaState` (auto-generated)

### Modified

- `src/Services/Order/UrbanX.Order.Persistence/OrderDbContext.cs` — thêm `DbSet`

## Implementation

### 1. PlaceSalesOrderSagaState

```csharp
namespace UrbanX.Order.Domain.Sagas;

public sealed class PlaceSalesOrderSagaState : SagaStateBase
{
    // Inherited: CorrelationId, CurrentState, CreatedAt, UpdatedAt, Version

    // Order identity
    public Guid OrderId { get; set; }              // = CorrelationId
    public string UserId { get; set; } = default!;
    public Guid CampaignId { get; set; }
    public string IdempotencyKey { get; set; } = default!;

    // Pricing snapshot
    public decimal Subtotal { get; set; }
    public decimal? FinalAmount { get; set; }

    // Side-effect tracking (for compensation)
    public Guid? ReservationId { get; set; }
    public Guid? CouponClaimId { get; set; }
    public string? ClaimedFlashSaleSlotsJson { get; set; }  // JSON serialized list
    public bool QuotaReserved { get; set; }
    public int QuotaQuantity { get; set; }

    // Failure info
    public string? FailureReason { get; set; }
    public string? FailureStep { get; set; }

    // Scheduled timeout tokens
    public Guid? TimeoutTokenId { get; set; }
}
```

### 2. PlaceSalesOrderSagaStateMachine

```csharp
namespace UrbanX.Order.Application.Sagas;

public sealed class PlaceSalesOrderSagaStateMachine : SagaStateMachineBase<PlaceSalesOrderSagaState>
{
    public State PromotionRedeeming { get; private set; } = default!;
    public State InventoryReserving { get; private set; } = default!;
    public State CouponClaiming { get; private set; } = default!;
    public State Confirming { get; private set; } = default!;
    public State Compensating { get; private set; } = default!;
    public State Confirmed { get; private set; } = default!;
    public State Failed { get; private set; } = default!;

    public Event<PlaceSalesOrderRequestedV1> Requested { get; private set; } = default!;
    public Event<PromotionRedeemedV1> PromotionRedeemed { get; private set; } = default!;
    public Event<PromotionRedeemFailedV1> PromotionRedeemFailed { get; private set; } = default!;
    public Event<InventoryReservedV1> InventoryReserved { get; private set; } = default!;
    public Event<InventoryReserveFailedV1> InventoryReserveFailed { get; private set; } = default!;
    public Event<CouponClaimedV1> CouponClaimed { get; private set; } = default!;
    public Event<CouponClaimFailedV1> CouponClaimFailed { get; private set; } = default!;

    public Schedule<PlaceSalesOrderSagaState, StateTimeoutV1> StateTimeout { get; private set; } = default!;

    public PlaceSalesOrderSagaStateMachine(ILogger<PlaceSalesOrderSagaStateMachine> logger) : base(logger)
    {
        InstanceState(x => x.CurrentState);

        // Correlate by OrderId
        Event(() => Requested, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => PromotionRedeemed, e => e.CorrelateById(c => c.Message.OrderId));
        // ... same for other events

        Schedule(() => StateTimeout, s => s.TimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(30);
            s.Received = r => r.CorrelateById(c => c.Message.OrderId);
        });

        Initially(
            When(Requested)
                .Then(ctx => /* snapshot data */)
                .Schedule(StateTimeout, ctx => new StateTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<RedeemSalePromotionRequestedV1>(/* ... */))
                .TransitionTo(PromotionRedeeming)
        );

        During(PromotionRedeeming,
            When(PromotionRedeemed)
                .Then(ctx => /* save discounts + slots */)
                .Unschedule(StateTimeout)
                .Schedule(StateTimeout, /* reset */)
                .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(/* ... */))
                .TransitionTo(InventoryReserving),
            When(PromotionRedeemFailed)
                .Then(ctx => { ctx.Saga.FailureStep = "PromotionRedeem"; ctx.Saga.FailureReason = ctx.Message.ErrorMessage; })
                .PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(/* ... */))
                .TransitionTo(Compensating),
            When(StateTimeout.Received)
                .Then(ctx => { ctx.Saga.FailureStep = "PromotionTimeout"; })
                .PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(/* ... */))
                .TransitionTo(Compensating)
        );

        During(InventoryReserving,
            When(InventoryReserved)
                .Then(ctx => ctx.Saga.ReservationId = ctx.Message.ReservationId)
                .IfElse(ctx => /* has coupon? */,
                    ifBranch => ifBranch
                        .PublishAsync(ctx => ctx.Init<ClaimCouponRequestedV1>(/* ... */))
                        .TransitionTo(CouponClaiming),
                    elseBranch => elseBranch
                        .TransitionTo(Confirming)),
            When(InventoryReserveFailed)
                .PublishAsync(/* FlashSaleSlotReleaseRequestedV1 per slot */)
                .PublishAsync(/* SaleQuotaReleaseRequestedV1 */)
                .TransitionTo(Compensating),
            When(StateTimeout.Received).TransitionTo(Compensating)
        );

        During(CouponClaiming,
            When(CouponClaimed)
                .Then(ctx => ctx.Saga.CouponClaimId = ctx.Message.ClaimId)
                .TransitionTo(Confirming),
            When(CouponClaimFailed)
                .PublishAsync(/* InventoryReleaseRequestedV1 */)
                .PublishAsync(/* FlashSaleSlotReleaseRequestedV1 per slot */)
                .PublishAsync(/* SaleQuotaReleaseRequestedV1 */)
                .TransitionTo(Compensating)
        );

        During(Confirming,
            Initial,
            Ignore(Requested) // idempotent — re-deliveries
        );

        // Confirming actions invoked by saga internal activity
        DuringAny(
            When(SomeInternalEvent)... // optional
        );

        During(Compensating,
            // wait for compensation completion (currently fire-and-forget, transition to Failed immediately)
        );

        SetCompletedWhenFinalized(); // remove saga state after Confirmed/Failed
    }
}

public record StateTimeoutV1 { public Guid OrderId { get; init; } }
```

> **Note**: Implementation chi tiết của Confirming activity (update Order entity + publish `PlaceSalesOrderConfirmedV1`) sẽ dùng MassTransit Activity hoặc Saga finalizer. Member chọn approach phù hợp.

### 3. EF configuration

```csharp
namespace UrbanX.Order.Persistence.Configurations;

internal sealed class PlaceSalesOrderSagaStateConfiguration
    : SagaStateEfCoreConfiguration<PlaceSalesOrderSagaState>
{
    protected override string TableName => "place_sales_order_saga_states";

    protected override void ConfigureCustom(EntityTypeBuilder<PlaceSalesOrderSagaState> builder)
    {
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.Property(x => x.FinalAmount).HasPrecision(18, 2);
        builder.Property(x => x.ClaimedFlashSaleSlotsJson).HasColumnType("jsonb");
        builder.Property(x => x.FailureReason).HasMaxLength(512);
        builder.Property(x => x.FailureStep).HasMaxLength(64);

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.CurrentState, x.CreatedAt });
    }
}
```

### 4. DbContext update

```csharp
// OrderDbContext.cs
public DbSet<PlaceSalesOrderSagaState> PlaceSalesOrderSagas => Set<PlaceSalesOrderSagaState>();
```

### 5. Migration

Từ `src/Services/Order/UrbanX.Order.Persistence/` directory:

```bash
dotnet ef migrations add AddPlaceSalesOrderSagaState
```

Kiểm tra migration script tạo table `place_sales_order_saga_states` với cột `version` (concurrency token).

## Acceptance criteria

- [ ] Build solution thành công.
- [ ] Migration generated, có table + indexes đúng spec.
- [ ] Unit test cover state diagram:
  - Happy path: Initial → PromotionRedeeming → InventoryReserving → Confirming → Confirmed.
  - Failure path: từng state có thể chuyển sang Compensating → Failed.
  - Timeout: mỗi state nhận `StateTimeout.Received` → transition Compensating.
- [ ] Test correlate event theo OrderId (multiple sagas không nhầm).

## Testing notes

- Dùng `MassTransit.Testing` package — `InMemoryTestHarness` với saga test harness.
- Test mỗi transition standalone: publish event đầu vào, assert state machine state + outbound events.
- Reference pattern: [MassTransit Saga Testing docs](https://masstransit.io/documentation/concepts/testing#saga-state-machine).

## Reference

- Base classes: [src/Shared/Shared.Messaging/Saga/](../../../src/Shared/Shared.Messaging/Saga/)
- `SagaStateBase`: [src/Shared/Shared.Messaging/Saga/SagaStateBase.cs](../../../src/Shared/Shared.Messaging/Saga/SagaStateBase.cs)
- `SagaStateMachineBase`: [src/Shared/Shared.Messaging/Saga/SagaStateMachineBase.cs](../../../src/Shared/Shared.Messaging/Saga/SagaStateMachineBase.cs)
- `SagaStateEfCoreConfiguration`: [src/Shared/Shared.Messaging/Saga/SagaStateEfCoreConfiguration.cs](../../../src/Shared/Shared.Messaging/Saga/SagaStateEfCoreConfiguration.cs)
- Compensation events đã có: `Shared.Contract/Messaging/PlaceOrder/`
