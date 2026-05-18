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
/// Orchestrates the place-sales-order flow:
///   Initial → PromotionRedeeming → InventoryReserving → [CouponClaiming] → PaymentProcessing → Confirming → PaymentPending → Finalized
///
/// On any failure the saga transitions to Compensating (publishes rollback events)
/// then immediately to Faulted.
///
/// Requires a MassTransit message scheduler (Quartz or InMemory) for per-step timeouts.
/// Register: bus.UseInMemoryScheduler() or bus.UseQuartzScheduler() in Program.cs.
/// </summary>
public sealed class PlaceSalesOrderSagaStateMachine : SagaStateMachineBase<PlaceSalesOrderSagaState>
{
    // ── Workflow states ───────────────────────────────────────────────────────
    public State PromotionRedeeming { get; private set; } = default!;
    public State InventoryReserving { get; private set; } = default!;
    public State CouponClaiming     { get; private set; } = default!;
    public State PaymentProcessing  { get; private set; } = default!;
    public State Confirming         { get; private set; } = default!;
    public State PaymentPending     { get; private set; } = default!;

    // ── Events ────────────────────────────────────────────────────────────────
    public Event<PlaceSalesOrderRequestedV1>  Requested              { get; private set; } = default!;
    public Event<PromotionRedeemedV1>         PromotionRedeemed      { get; private set; } = default!;
    public Event<PromotionRedeemFailedV1>     PromotionRedeemFailed  { get; private set; } = default!;
    public Event<InventoryReservedV1>         InventoryReserved      { get; private set; } = default!;
    public Event<InventoryReserveFailedV1>    InventoryReserveFailed { get; private set; } = default!;
    public Event<CouponClaimedV1>             CouponClaimed          { get; private set; } = default!;
    public Event<CouponClaimFailedV1>         CouponClaimFailed      { get; private set; } = default!;
    public Event<PaymentProcessedV1>          PaymentProcessed       { get; private set; } = default!;
    public Event<PaymentProcessFailedV1>      PaymentProcessFailed   { get; private set; } = default!;
    public Event<PaymentSessionCreatedV1>     PaymentSessionCreated  { get; private set; } = default!;
    public Event<PaymentSessionCompletedV1>   PaymentCompleted       { get; private set; } = default!;

    // ── Schedules ─────────────────────────────────────────────────────────────
    public Schedule<PlaceSalesOrderSagaState, SagaStepTimeoutV1>      StepTimeout   { get; private set; } = default!;
    public Schedule<PlaceSalesOrderSagaState, PaymentExpiryTimeoutV1> PaymentExpiry { get; private set; } = default!;

    private readonly IServiceScopeFactory _scopeFactory;

    public PlaceSalesOrderSagaStateMachine(
        IOptions<OrderPaymentOptions> paymentOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<PlaceSalesOrderSagaStateMachine> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;

        InstanceState(x => x.CurrentState);

        ConfigureEvents();
        ConfigureSchedules(paymentOptions.Value);

        // ── Initial ───────────────────────────────────────────────────────────
        Initially(
            When(Requested)
                .Then(SnapshotRequest)
                .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<RedeemSalePromotionRequestedV1>(new
                {
                    CorrelationId = ctx.Saga.OrderId.ToString("D"),
                    ctx.Saga.OrderId,
                    ctx.Saga.UserId,
                    ctx.Saga.CampaignId,
                    ctx.Saga.CouponCode,
                    ctx.Saga.Subtotal,
                    Items = (IReadOnlyList<PromotionOrderItem>)ctx.Message.Items
                        .Select(i => new PromotionOrderItem(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice))
                        .ToList()
                }))
                .TransitionTo(PromotionRedeeming));

        // ── PromotionRedeeming ─────────────────────────────────────────────────
        During(PromotionRedeeming,
            When(PromotionRedeemed)
                .Then(ctx =>
                {
                    ctx.Saga.PromotionDiscount = ctx.Message.OrderLevelDiscount
                        + ctx.Message.ItemDiscounts.Sum(d => d.DiscountAmount);
                    ctx.Saga.ClaimedFlashSaleSlotsJson =
                        JsonSerializer.Serialize(ctx.Message.ClaimedFlashSaleSlots);
                    ctx.Saga.QuotaReserved = true;
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
                        .PublishAsync(ctx => ctx.Init<ClaimCouponRequestedV1>(new
                        {
                            CorrelationId       = ctx.Saga.OrderId.ToString("D"),
                            ctx.Saga.OrderId,
                            OrderIdempotencyKey = $"{ctx.Saga.IdempotencyKey}:cpn",
                            ctx.Saga.UserId,
                            CouponCode          = ctx.Saga.CouponCode!,
                            OrderTotal          = ctx.Saga.Subtotal - ctx.Saga.PromotionDiscount + ctx.Saga.ShippingFee
                        }))
                        .TransitionTo(CouponClaiming),
                    noCoupon => noCoupon
                        .Then(ctx => { ctx.Saga.CouponDiscount = 0; StampInstance(ctx.Saga); })
                        .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .PublishAsync(ctx => ctx.Init<ProcessPaymentRequestedV1>(BuildPaymentRequest(ctx.Saga)))
                        .TransitionTo(PaymentProcessing)),

            When(InventoryReserveFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "InventoryReserve";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
                .TransitionTo(Compensating),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "InventoryTimeout";
                    ctx.Saga.FailureReason = "Inventory service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
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
                .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<ProcessPaymentRequestedV1>(BuildPaymentRequest(ctx.Saga)))
                .TransitionTo(PaymentProcessing),

            When(CouponClaimFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "CouponClaim";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(new
                {
                    CorrelationId = ctx.Saga.OrderId.ToString("D"),
                    ReservationId = ctx.Saga.ReservationId!.Value,
                    Reason        = ctx.Saga.FailureReason
                }))
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
                .TransitionTo(Compensating),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "CouponTimeout";
                    ctx.Saga.FailureReason = "Coupon service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(new
                {
                    CorrelationId = ctx.Saga.OrderId.ToString("D"),
                    ReservationId = ctx.Saga.ReservationId!.Value,
                    Reason        = ctx.Saga.FailureReason
                }))
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
                .TransitionTo(Compensating));

        // ── PaymentProcessing ──────────────────────────────────────────────────
        During(PaymentProcessing,
            When(PaymentProcessed)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentId = ctx.Message.PaymentId;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .PublishAsync(ctx => ctx.Init<PlaceSalesOrderConfirmedV1>(BuildConfirmedEvent(ctx.Saga)))
                .TransitionTo(Confirming),

            When(PaymentProcessFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PaymentProcess";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(StepTimeout)
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(new
                {
                    CorrelationId = ctx.Saga.OrderId.ToString("D"),
                    ReservationId = ctx.Saga.ReservationId!.Value,
                    Reason        = ctx.Saga.FailureReason
                }))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(new
                    {
                        CorrelationId = ctx.Saga.OrderId.ToString("D"),
                        ClaimId       = ctx.Saga.CouponClaimId!.Value,
                        Reason        = ctx.Saga.FailureReason
                    })))
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
                .TransitionTo(Compensating),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PaymentTimeout";
                    ctx.Saga.FailureReason = "Payment service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(new
                {
                    CorrelationId = ctx.Saga.OrderId.ToString("D"),
                    ReservationId = ctx.Saga.ReservationId!.Value,
                    Reason        = ctx.Saga.FailureReason
                }))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(new
                    {
                        CorrelationId = ctx.Saga.OrderId.ToString("D"),
                        ClaimId       = ctx.Saga.CouponClaimId!.Value,
                        Reason        = ctx.Saga.FailureReason
                    })))
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
                .TransitionTo(Compensating));

        // ── Confirming → PaymentPending ────────────────────────────────────────
        WhenEnter(Confirming, binder => binder
            .Schedule(PaymentExpiry, ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
            .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(new
            {
                CorrelationId  = ctx.Saga.OrderId.ToString("D"),
                ctx.Saga.OrderId,
                IdempotencyKey = $"{ctx.Saga.IdempotencyKey}:pay",
                Amount         = ctx.Saga.Subtotal - ctx.Saga.PromotionDiscount
                                 - ctx.Saga.CouponDiscount + ctx.Saga.ShippingFee,
                Currency       = "VND"
            }))
            .TransitionTo(PaymentPending));

        // ── PaymentPending ─────────────────────────────────────────────────────
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
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(new
                {
                    CorrelationId = ctx.Saga.OrderId.ToString("D"),
                    ReservationId = ctx.Saga.ReservationId!.Value,
                    Reason        = ctx.Saga.FailureReason
                }))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(new
                    {
                        CorrelationId = ctx.Saga.OrderId.ToString("D"),
                        ClaimId       = ctx.Saga.CouponClaimId!.Value,
                        Reason        = ctx.Saga.FailureReason
                    })))
                .If(ctx => ctx.Saga.QuotaReserved,
                    b => b.PublishAsync(ctx => ctx.Init<SaleQuotaReleaseRequestedV1>(BuildQuotaRelease(ctx.Saga))))
                .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired"))
                .TransitionTo(Faulted));

        // ── Compensating ──────────────────────────────────────────────────────
        WhenEnter(Compensating, binder => binder
            .PublishAsync(ctx => ctx.Init<PlaceSalesOrderFailedV1>(new
            {
                CorrelationId = ctx.Saga.OrderId.ToString("D"),
                ctx.Saga.OrderId,
                ctx.Saga.UserId,
                FailureStep   = ctx.Saga.FailureStep ?? "Unknown",
                FailureReason = ctx.Saga.FailureReason ?? "An unexpected error occurred.",
                OccurredAt    = DateTimeOffset.UtcNow
            }))
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
        Event(() => PaymentProcessed,       e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentProcessFailed,   e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSessionCreated,  e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentCompleted,       e => e.CorrelateById(ctx => ctx.Message.OrderId));
    }

    private void ConfigureSchedules(OrderPaymentOptions opts)
    {
        Schedule(() => StepTimeout, x => x.TimeoutTokenId, cfg =>
        {
            cfg.Delay    = TimeSpan.FromSeconds(30);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
        {
            cfg.Delay    = TimeSpan.FromMinutes(opts.SalesOrderExpiryMinutes);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });
    }

    // ── Domain operations ────────────────────────────────────────────────────

    private async Task SetPaymentSessionAsync(PlaceSalesOrderSagaState saga, string paymentUrl, string? qrCodeUrl)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId);
            if (order is null || saga.ReservationId is null) return;
            order.MarkReadyForPayment(
                saga.ReservationId.Value,
                saga.CouponClaimId,
                paymentUrl,
                qrCodeUrl,
                Guid.Parse(saga.UserId),
                string.Empty);
        });
    }

    private async Task MarkOrderPaidAsync(PlaceSalesOrderSagaState saga, string paymentSessionId)
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

    private async Task CancelOrderAsync(PlaceSalesOrderSagaState saga, string reason)
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

    // ── Message factories ────────────────────────────────────────────────────

    private static object BuildInventoryRequest(PlaceSalesOrderSagaState saga)
    {
        var items = JsonSerializer.Deserialize<List<OrderItemSnapshot>>(saga.ItemsJson!)!
            .Select(i => new InventoryReserveItem(i.ProductId, i.VariantId, i.Quantity))
            .ToList();

        return new
        {
            CorrelationId       = saga.OrderId.ToString("D"),
            saga.OrderId,
            OrderIdempotencyKey = $"{saga.IdempotencyKey}:inv",
            Items               = (IReadOnlyList<InventoryReserveItem>)items
        };
    }

    private static object BuildPaymentRequest(PlaceSalesOrderSagaState saga) => new
    {
        CorrelationId       = saga.OrderId.ToString("D"),
        saga.OrderId,
        saga.UserId,
        OrderIdempotencyKey = $"{saga.IdempotencyKey}:pay",
        FinalAmount         = saga.Subtotal - saga.PromotionDiscount - saga.CouponDiscount + saga.ShippingFee,
        saga.CampaignId,
        ReservationId       = saga.ReservationId!.Value,
        ClaimId             = saga.CouponClaimId
    };

    private static object BuildConfirmedEvent(PlaceSalesOrderSagaState saga) => new
    {
        CorrelationId = saga.OrderId.ToString("D"),
        saga.OrderId,
        saga.UserId,
        saga.CampaignId,
        ReservationId = saga.ReservationId!.Value,
        ClaimId       = saga.CouponClaimId,
        PaymentId     = saga.PaymentId!.Value,
        FinalAmount   = saga.Subtotal - saga.PromotionDiscount - saga.CouponDiscount + saga.ShippingFee,
        ConfirmedAt   = DateTimeOffset.UtcNow
    };

    private static object BuildQuotaRelease(PlaceSalesOrderSagaState saga) => new
    {
        CorrelationId = saga.OrderId.ToString("D"),
        saga.CampaignId,
        UserId        = Guid.Parse(saga.UserId),
        saga.QuotaQuantity,
        QuotaKey      = $"campaign:{saga.CampaignId}"
    };

    // ── Snapshot helper ──────────────────────────────────────────────────────

    private static void SnapshotRequest(
        BehaviorContext<PlaceSalesOrderSagaState, PlaceSalesOrderRequestedV1> ctx)
    {
        var msg  = ctx.Message;
        var saga = ctx.Saga;

        saga.OrderId        = msg.OrderId;
        saga.UserId         = msg.UserId;
        saga.CampaignId     = msg.CampaignId;
        saga.IdempotencyKey = msg.IdempotencyKey;
        saga.Subtotal       = msg.Subtotal;
        saga.ShippingFee    = msg.ShippingFee;
        saga.CouponCode     = msg.CouponCode;
        saga.ItemsJson      = JsonSerializer.Serialize(msg.Items);
        saga.QuotaQuantity  = msg.Items.Sum(i => i.Quantity);
    }
}

/// <summary>Timeout message correlated by OrderId; fires after each step's 30 s window.</summary>
public record SagaStepTimeoutV1
{
    public Guid OrderId { get; init; }
}
