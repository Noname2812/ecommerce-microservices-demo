using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contract.Messaging.Payment;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using Shared.Messaging.Saga;
using System.Text.Json;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Sagas;

/// <summary>
/// Orchestrates the place-normal-order flow (async):
///   Initial → [PromotionRedeeming] → InventoryReserving → [CouponClaiming] → PaymentPending → Finalized
///   On failure → Compensating (release inventory + coupon if acquired) → Faulted
/// </summary>
public sealed class PlaceOrderNormalSagaStateMachine
    : SagaStateMachineBase<PlaceOrderNormalSagaState>
{
    // ── Workflow states ───────────────────────────────────────────────────────
    public State PromotionRedeeming { get; private set; } = default!;
    public State InventoryReserving { get; private set; } = default!;
    public State CouponClaiming     { get; private set; } = default!;
    public State PaymentPending     { get; private set; } = default!;

    // ── Events ────────────────────────────────────────────────────────────────
    public Event<PlaceOrderRequestedV1>                    Requested              { get; private set; } = default!;
    public Event<NormalOrderPromotionRedeemedV1>           PromotionRedeemed      { get; private set; } = default!;
    public Event<NormalOrderPromotionRedeemFailedV1>       PromotionRedeemFailed  { get; private set; } = default!;
    public Event<InventoryReservedV1>                      InventoryReserved      { get; private set; } = default!;
    public Event<InventoryReserveFailedV1>                 InventoryReserveFailed { get; private set; } = default!;
    public Event<CouponClaimedV1>                          CouponClaimed          { get; private set; } = default!;
    public Event<CouponClaimFailedV1>                      CouponClaimFailed      { get; private set; } = default!;
    public Event<PaymentSessionCreatedV1>                  PaymentSessionCreated  { get; private set; } = default!;
    public Event<PaymentSessionCompletedV1>                PaymentCompleted       { get; private set; } = default!;

    // ── Schedules ─────────────────────────────────────────────────────────────
    public Schedule<PlaceOrderNormalSagaState, SagaStepTimeoutV1>     StepTimeout    { get; private set; } = default!;
    public Schedule<PlaceOrderNormalSagaState, PaymentExpiryTimeoutV1> PaymentExpiry { get; private set; } = default!;

    private readonly IServiceScopeFactory _scopeFactory;

    public PlaceOrderNormalSagaStateMachine(
        IOptions<OrderPaymentOptions> paymentOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<PlaceOrderNormalSagaStateMachine> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;

        InstanceState(x => x.CurrentState);

        ConfigureEvents();
        ConfigureSchedules(paymentOptions.Value);

        // ── Initially ─────────────────────────────────────────────────────────
        Initially(
            When(Requested)
                .Then(ctx => SnapshotRequest(ctx.Saga, ctx.Message))
                .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .IfElse(
                    ctx => ctx.Saga.CouponCode != null,
                    hasCoupon => hasCoupon
                        .PublishAsync(ctx => ctx.Init<RedeemPromotionForNormalOrderRequestedV1>(
                            BuildPromotionRequest(ctx.Saga)))
                        .TransitionTo(PromotionRedeeming),
                    noCoupon => noCoupon
                        .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(
                            BuildInventoryRequest(ctx.Saga)))
                        .TransitionTo(InventoryReserving)));

        // ── PromotionRedeeming ─────────────────────────────────────────────────
        During(PromotionRedeeming,
            When(PromotionRedeemed)
                .Then(ctx =>
                {
                    ctx.Saga.PromotionDiscount = ctx.Message.OrderLevelDiscount
                        + ctx.Message.ItemDiscounts.Sum(d => d.DiscountAmount);
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(BuildInventoryRequest(ctx.Saga)))
                .TransitionTo(InventoryReserving),

            When(PromotionRedeemFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PromotionRedeem";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .TransitionTo(Compensating),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PromotionTimeout";
                    ctx.Saga.FailureReason = "Promotion service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .TransitionTo(Compensating));

        // ── InventoryReserving ─────────────────────────────────────────────────
        During(InventoryReserving,
            When(InventoryReserved)
                .Then(ctx =>
                {
                    ctx.Saga.ReservationId = ctx.Message.ReservationId;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .IfElse(
                    ctx => ctx.Saga.CouponCode != null,
                    hasCoupon => hasCoupon
                        .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .PublishAsync(ctx => ctx.Init<ClaimCouponRequestedV1>(BuildCouponRequest(ctx.Saga)))
                        .TransitionTo(CouponClaiming),
                    noCoupon => noCoupon
                        .Then(ctx => { ctx.Saga.CouponDiscount = 0; StampInstance(ctx.Saga); })
                        .TransitionTo(PaymentPending)),

            When(InventoryReserveFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "InventoryReserve";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .TransitionTo(Compensating),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "InventoryTimeout";
                    ctx.Saga.FailureReason = "Inventory service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .TransitionTo(Compensating));

        // ── CouponClaiming ─────────────────────────────────────────────────────
        During(CouponClaiming,
            When(CouponClaimed)
                .Then(ctx =>
                {
                    ctx.Saga.CouponClaimId  = ctx.Message.ClaimId;
                    ctx.Saga.CouponDiscount = ctx.Message.DiscountAmount;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .TransitionTo(PaymentPending),

            When(CouponClaimFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "CouponClaim";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .TransitionTo(Compensating),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "CouponTimeout";
                    ctx.Saga.FailureReason = "Coupon service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .TransitionTo(Compensating));

        // ── PaymentPending — entered via WhenEnter, events handled via During ──
        WhenEnter(PaymentPending, binder => binder
            .ThenAsync(ctx => ConfirmOrderAsync(ctx.Saga))
            .Schedule(PaymentExpiry, ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
            .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(new
            {
                CorrelationId  = ctx.Saga.OrderId.ToString("D"),
                ctx.Saga.OrderId,
                IdempotencyKey = $"{ctx.Saga.IdempotencyKey}:pay",
                Amount         = ctx.Saga.Subtotal - ctx.Saga.PromotionDiscount
                                 - ctx.Saga.CouponDiscount + ctx.Saga.ShippingFee,
                Currency       = "VND"
            })));

        During(PaymentPending,
            When(PaymentSessionCreated)
                .ThenAsync(ctx => SetPaymentSessionAsync(
                    ctx.Saga, ctx.Message.PaymentUrl, ctx.Message.QrCodeUrl))
                .Then(ctx =>
                {
                    ctx.Saga.PaymentSessionId = ctx.Message.PaymentSessionId;
                    StampInstance(ctx.Saga);
                }),

            When(PaymentCompleted)
                .Unschedule(PaymentExpiry)
                .ThenAsync(ctx => MarkOrderPaidAsync(ctx.Saga, ctx.Message.PaymentSessionId))
                .Finalize(),

            When(PaymentExpiry.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PaymentExpiry";
                    ctx.Saga.FailureReason = "Payment window expired.";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
                .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired"))
                .TransitionTo(Faulted));

        // ── Compensating — cancel order (inventory/coupon releases already published by caller) ──
        WhenEnter(Compensating, binder => binder
            .If(ctx => ctx.Saga.ReservationId.HasValue && ctx.Saga.FailureStep != "CouponClaim"
                                                       && ctx.Saga.FailureStep != "CouponTimeout",
                b => b.PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga))))
            .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
            .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, ctx.Saga.FailureReason ?? "Order failed"))
            .TransitionTo(Faulted));

        SetCompletedWhenFinalized();
        RegisterStateLogging();
    }

    // ── Event & schedule configuration ──────────────────────────────────────

    private void ConfigureEvents()
    {
        Event(() => Requested,              e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PromotionRedeemed,      e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PromotionRedeemFailed,  e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserved,      e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserveFailed, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => CouponClaimed,          e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => CouponClaimFailed,      e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSessionCreated,  e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentCompleted,       e => e.CorrelateById(ctx => ctx.Message.OrderId));
    }

    private void ConfigureSchedules(OrderPaymentOptions opts)
    {
        Schedule(() => StepTimeout, x => x.StepTimeoutTokenId, cfg =>
        {
            cfg.Delay    = TimeSpan.FromSeconds(30);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
        {
            cfg.Delay    = TimeSpan.FromMinutes(opts.NormalOrderExpiryMinutes);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });
    }

    // ── Domain operations (create scope to avoid captive scoped dependency) ──

    private async Task ConfirmOrderAsync(PlaceOrderNormalSagaState saga)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId);
            if (order is null) return;
            var userId = Guid.Parse(saga.UserId);
            order.SetConfirmedWithReservation(saga.ReservationId!.Value, saga.CouponClaimId, userId, string.Empty);
        });
    }

    private async Task SetPaymentSessionAsync(PlaceOrderNormalSagaState saga, string paymentUrl, string? qrCodeUrl)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId);
            order?.SetPaymentSession(paymentUrl, qrCodeUrl);
        });
    }

    private async Task MarkOrderPaidAsync(PlaceOrderNormalSagaState saga, string paymentSessionId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId);
            if (order is null) return;
            var userId = Guid.Parse(saga.UserId);
            order.MarkPaid(paymentSessionId, userId, string.Empty);
        });
    }

    private async Task CancelOrderAsync(PlaceOrderNormalSagaState saga, string reason)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId);
            if (order is null) return;
            var userId = Guid.TryParse(saga.UserId, out var uid) ? uid : (Guid?)null;
            order.Cancel(reason, userId, null);
        });
    }

    // ── Snapshot helper ──────────────────────────────────────────────────────

    private static void SnapshotRequest(PlaceOrderNormalSagaState saga, PlaceOrderRequestedV1 msg)
    {
        saga.CorrelationId = msg.OrderId;
        saga.OrderId       = msg.OrderId;
        saga.UserId        = msg.UserId;
        saga.IdempotencyKey = msg.IdempotencyKey;
        saga.CouponCode    = msg.CouponCode;
        saga.Subtotal      = msg.Subtotal;
        saga.ShippingFee   = msg.ShippingFee;
        saga.ItemsJson     = JsonSerializer.Serialize(msg.Items);
    }

    // ── Message factories ────────────────────────────────────────────────────

    private static object BuildPromotionRequest(PlaceOrderNormalSagaState saga)
    {
        var items = JsonSerializer.Deserialize<List<NormalOrderItemSnapshot>>(saga.ItemsJson!)!
            .Select(i => new NormalOrderPromotionItem(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice))
            .ToList();

        return new
        {
            CorrelationId = saga.OrderId.ToString("D"),
            saga.OrderId,
            saga.UserId,
            CouponCode = saga.CouponCode!,
            saga.Subtotal,
            Items = (IReadOnlyList<NormalOrderPromotionItem>)items
        };
    }

    private static object BuildInventoryRequest(PlaceOrderNormalSagaState saga)
    {
        var items = JsonSerializer.Deserialize<List<NormalOrderItemSnapshot>>(saga.ItemsJson!)!
            .Select(i => new InventoryReserveItem(i.ProductId, i.VariantId, i.Quantity))
            .ToList();

        return new
        {
            CorrelationId      = saga.OrderId.ToString("D"),
            saga.OrderId,
            OrderIdempotencyKey = $"{saga.IdempotencyKey}:inv",
            Items              = (IReadOnlyList<InventoryReserveItem>)items
        };
    }

    private static object BuildCouponRequest(PlaceOrderNormalSagaState saga) => new
    {
        CorrelationId       = saga.OrderId.ToString("D"),
        saga.OrderId,
        OrderIdempotencyKey = $"{saga.IdempotencyKey}:cpn",
        saga.UserId,
        CouponCode          = saga.CouponCode!,
        OrderTotal          = saga.Subtotal - saga.PromotionDiscount + saga.ShippingFee
    };

    private static object BuildInventoryRelease(PlaceOrderNormalSagaState saga) => new
    {
        CorrelationId = saga.OrderId.ToString("D"),
        ReservationId = saga.ReservationId!.Value,
        Reason        = saga.FailureReason
    };

    private static object BuildCouponRelease(PlaceOrderNormalSagaState saga) => new
    {
        CorrelationId = saga.OrderId.ToString("D"),
        ClaimId       = saga.CouponClaimId!.Value,
        Reason        = saga.FailureReason
    };
}
