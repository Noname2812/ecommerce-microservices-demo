using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contract.Messaging.Order;
using Shared.Contract.Messaging.Payment;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using Shared.Messaging;
using Shared.Messaging.Saga;
using System.Text.Json;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.DependencyInjection.Options;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Application.Usecases.V1.Command.Common;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Sagas;

/// <summary>
/// Orchestrates the place-normal-order flow:
///   Initial → ValidateCatalog → OrderPersist → InventoryReserving
///   → [CouponClaiming] → PaymentSessionCreating → PaymentPending → Finalized
///   On failure → Compensating (releases already-acquired resources) → Faulted
/// </summary>
public sealed class PlaceOrderNormalSagaStateMachine
    : SagaStateMachineBase<PlaceOrderNormalSagaState>
{
    // ── Workflow states ───────────────────────────────────────────────────────
    public State InventoryReserving     { get; private set; } = default!;
    public State CouponClaiming         { get; private set; } = default!;
    public State PaymentSessionCreating { get; private set; } = default!;
    public State PaymentPending         { get; private set; } = default!;

    // ── Events ────────────────────────────────────────────────────────────────
    public Event<PlaceOrderRequestedV1>     Requested              { get; private set; } = default!;
    public Event<InventoryReservedV1>       InventoryReserved      { get; private set; } = default!;
    public Event<InventoryReserveFailedV1>  InventoryReserveFailed { get; private set; } = default!;
    public Event<CouponClaimedV1>           CouponClaimed          { get; private set; } = default!;
    public Event<CouponClaimFailedV1>       CouponClaimFailed      { get; private set; } = default!;
    public Event<PaymentSessionCreatedV1>   PaymentSessionCreated  { get; private set; } = default!;
    public Event<PaymentSessionCompletedV1> PaymentCompleted       { get; private set; } = default!;

    // ── Schedules ─────────────────────────────────────────────────────────────
    public Schedule<PlaceOrderNormalSagaState, SagaStepTimeoutV1>      StepTimeout   { get; private set; } = default!;
    public Schedule<PlaceOrderNormalSagaState, PaymentExpiryTimeoutV1> PaymentExpiry { get; private set; } = default!;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrderPaymentOptions  _paymentOptions;

    public PlaceOrderNormalSagaStateMachine(
        IOptions<OrderPaymentOptions> paymentOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<PlaceOrderNormalSagaStateMachine> logger)
        : base(logger)
    {
        _scopeFactory   = scopeFactory;
        _paymentOptions = paymentOptions.Value;

        InstanceState(x => x.CurrentState);

        ConfigureEvents();
        ConfigureSchedules();

        // ── Initially ─────────────────────────────────────────────────────────
        Initially(
            When(Requested)
                .Then(ctx => SnapshotRequest(ctx.Saga, ctx.Message))
                .ThenAsync(ctx => ValidateThroughCatalogAsync(ctx))
                .IfElse(
                    ctx => ctx.Saga.ValidationError != null,
                    fail => fail
                        .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                        .ThenAsync(ctx => PublishOrderCancelledAsync(ctx.Saga, ctx.Saga.ValidationError!, ctx.CancellationToken))
                        .TransitionTo(Faulted),
                    ok => ok
                        .ThenAsync(ctx => CreateOrderProcessingAsync(ctx))
                        .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(BuildInventoryRequest(ctx.Saga)))
                        .TransitionTo(InventoryReserving)));

        // ── InventoryReserving ─────────────────────────────────────────────────
        During(InventoryReserving,
            When(InventoryReserved)
                .Then(ctx => { ctx.Saga.ReservationId = ctx.Message.ReservationId; StampInstance(ctx.Saga); })
                .Unschedule(StepTimeout)
                .IfElse(
                    ctx => ctx.Saga.CouponCode != null,
                    hasCoupon => hasCoupon
                        .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .PublishAsync(ctx => ctx.Init<ClaimCouponRequestedV1>(BuildCouponRequest(ctx.Saga)))
                        .TransitionTo(CouponClaiming),
                    noCoupon => noCoupon
                        .TransitionTo(PaymentSessionCreating)),

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
                    ctx.Saga.FailureReason = "Inventory timeout";
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
                .TransitionTo(PaymentSessionCreating),

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
                    ctx.Saga.FailureReason = "Coupon timeout";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .TransitionTo(Compensating));

        // ── PaymentSessionCreating ─────────────────────────────────────────────
        WhenEnter(PaymentSessionCreating, b => b
            .Schedule(StepTimeout, ctx => new SagaStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
            .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(BuildPaymentSessionRequest(ctx.Saga))));

        During(PaymentSessionCreating,
            When(PaymentSessionCreated)
                .Unschedule(StepTimeout)
                .ThenAsync(ctx => MarkReadyForPaymentAsync(ctx))
                .IfElse(
                    // MarkReadyForPaymentAsync sets FailureStep when order not found or UserId invalid
                    ctx => ctx.Saga.FailureStep != null,
                    fail => fail
                        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                        .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                            b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
                        .ThenAsync(ctx => CancelOrderAsync(
                            ctx.Saga,
                            ctx.Saga.FailureReason ?? "Failed to mark order ready for payment",
                            ctx.CancellationToken))
                        .ThenAsync(ctx => PublishOrderCancelledAsync(
                            ctx.Saga,
                            ctx.Saga.FailureReason ?? "Failed to mark order ready for payment",
                            ctx.CancellationToken))
                        .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                        .TransitionTo(Faulted),
                    ok => ok
                        .Schedule(PaymentExpiry, ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .TransitionTo(PaymentPending)),

            When(StepTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PaymentSessionTimeout";
                    ctx.Saga.FailureReason = "Payment session create timeout";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
                .TransitionTo(Compensating));

        // ── PaymentPending ─────────────────────────────────────────────────────
        During(PaymentPending,
            When(PaymentCompleted)
                .Unschedule(PaymentExpiry)
                .ThenAsync(ctx => MarkOrderPaidAsync(ctx))
                .PublishAsync(ctx => ctx.Init<ConfirmInventoryRequestedV1>(BuildConfirmInventoryRequest(ctx.Saga)))
                .ThenAsync(ctx => PublishOrderConfirmedAsync(ctx.Saga, ctx.CancellationToken))
                .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                .Finalize(),

            When(PaymentExpiry.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PaymentExpiry";
                    ctx.Saga.FailureReason = "Payment expired";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .If(ctx => ctx.Saga.CouponClaimId.HasValue,
                    b => b.PublishAsync(ctx => ctx.Init<CouponReleaseRequestedV1>(BuildCouponRelease(ctx.Saga))))
                .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired", ctx.CancellationToken))
                .ThenAsync(ctx => PublishOrderCancelledAsync(ctx.Saga, "Payment expired", ctx.CancellationToken))
                .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                .TransitionTo(Faulted));

        // ── Compensating ─────────────────────────────────────────────────────
        // CouponClaim / CouponTimeout / PaymentSessionTimeout already published
        // InventoryReleaseRequestedV1 inline — skip here to avoid double-release.
        WhenEnter(Compensating, b => b
            .If(ctx => ctx.Saga.ReservationId.HasValue
                       && ctx.Saga.FailureStep != "CouponClaim"
                       && ctx.Saga.FailureStep != "CouponTimeout"
                       && ctx.Saga.FailureStep != "PaymentSessionTimeout",
                x => x.PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga))))
            .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, ctx.Saga.FailureReason ?? "Order failed", ctx.CancellationToken))
            .ThenAsync(ctx => PublishOrderCancelledAsync(ctx.Saga, ctx.Saga.FailureReason ?? "Order failed", ctx.CancellationToken))
            .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
            .TransitionTo(Faulted));

        SetCompletedWhenFinalized();
        RegisterStateLogging();
    }

    // ── Event & schedule configuration ───────────────────────────────────────

    private void ConfigureEvents()
    {
        Event(() => Requested,              e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserved,      e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserveFailed, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => CouponClaimed,          e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => CouponClaimFailed,      e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSessionCreated,  e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentCompleted,       e => e.CorrelateById(ctx => ctx.Message.OrderId));
    }

    private void ConfigureSchedules()
    {
        Schedule(() => StepTimeout, x => x.StepTimeoutTokenId, cfg =>
        {
            cfg.Delay    = TimeSpan.FromSeconds(30);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
        {
            cfg.Delay    = TimeSpan.FromMinutes(_paymentOptions.NormalOrderExpiryMinutes);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });
    }

    // ── Saga domain operations ────────────────────────────────────────────────

    private async Task ValidateThroughCatalogAsync(
        BehaviorContext<PlaceOrderNormalSagaState, PlaceOrderRequestedV1> ctx)
    {
        await using var scope  = _scopeFactory.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogServiceClient>();

        var variantIds = ctx.Message.Items.Select(i => i.VariantId).Distinct().ToArray();
        var result     = await catalog.GetVariantsAsync(variantIds, ctx.CancellationToken);

        if (result.IsFailure)
        {
            ctx.Saga.ValidationError = result.Error.Code == OrderErrors.CatalogUnavailable.Code
                ? "CATALOG_UNAVAILABLE"
                : "VARIANT_VALIDATION_FAILED";
            StampInstance(ctx.Saga);
            return;
        }

        var validation = ValidateBusinessRules(result.Value!, ctx.Message.Items);
        if (validation.IsFailure)
        {
            ctx.Saga.ValidationError = validation.Error.Code;
            StampInstance(ctx.Saga);
            return;
        }

        ctx.Saga.VariantsJson = JsonSerializer.Serialize(result.Value);
        StampInstance(ctx.Saga);
    }

    private async Task CreateOrderProcessingAsync(
        BehaviorContext<PlaceOrderNormalSagaState, PlaceOrderRequestedV1> ctx)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var existing = await repo.GetByIdempotencyKeyAsync(ctx.Saga.IdempotencyKey, ctx.CancellationToken);
        if (existing is not null)
        {
            Logger.LogInformation(
                "Order already exists for idempotency key {IdempotencyKey}, skipping create (order {OrderId})",
                ctx.Saga.IdempotencyKey,
                existing.Id);
            return;
        }

        // Set only when ValidateThroughCatalogAsync succeeded (ValidationError is null).
        var variants = JsonSerializer.Deserialize<List<CatalogVariantInfo>>(ctx.Saga.VariantsJson!)!
            .ToDictionary(v => v.VariantId);

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = OrderFactory.BuildFromSaga(ctx.Saga, variants, ctx.Saga.OrderId);
            repo.Add(order);
        }, ctx.CancellationToken);
    }

    private async Task MarkReadyForPaymentAsync(
        BehaviorContext<PlaceOrderNormalSagaState, PaymentSessionCreatedV1> ctx)
    {
        if (!Guid.TryParse(ctx.Saga.UserId, out var userId))
        {
            ctx.Saga.FailureStep   = "InvalidUserId";
            ctx.Saga.FailureReason = $"UserId '{ctx.Saga.UserId}' is not a valid GUID";
            StampInstance(ctx.Saga);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(ctx.Saga.OrderId, ctx.CancellationToken);
            if (order is null)
            {
                ctx.Saga.FailureStep   = "OrderNotFound";
                ctx.Saga.FailureReason = $"Order {ctx.Saga.OrderId} not found during MarkReadyForPayment";
                StampInstance(ctx.Saga);
                return;
            }

            order.MarkReadyForPayment(
                ctx.Saga.ReservationId!.Value,
                ctx.Saga.CouponClaimId,
                ctx.Message.PaymentUrl,
                ctx.Message.QrCodeUrl,
                userId,
                string.Empty);
        }, ctx.CancellationToken);

        // Only stamp payment fields when the order was found and updated
        if (ctx.Saga.FailureStep is null)
        {
            ctx.Saga.PaymentSessionId = ctx.Message.PaymentSessionId;
            ctx.Saga.PaymentUrl       = ctx.Message.PaymentUrl;
            ctx.Saga.QrCodeUrl        = ctx.Message.QrCodeUrl;
            ctx.Saga.PaymentExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_paymentOptions.NormalOrderExpiryMinutes);
            StampInstance(ctx.Saga);
        }
    }

    private async Task MarkOrderPaidAsync(
        BehaviorContext<PlaceOrderNormalSagaState, PaymentSessionCompletedV1> ctx)
    {
        if (!Guid.TryParse(ctx.Saga.UserId, out var userId)) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(ctx.Saga.OrderId, ctx.CancellationToken);
            if (order is null) return;
            order.MarkPaid(ctx.Message.PaymentSessionId, userId, string.Empty);
        }, ctx.CancellationToken);
    }

    private async Task CancelOrderAsync(PlaceOrderNormalSagaState saga, string reason, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId, ct);
            if (order is null) return;
            var userId = Guid.TryParse(saga.UserId, out var uid) ? uid : (Guid?)null;
            order.Cancel(reason, userId, null);
        }, ct);
    }

    private async Task ReleasePendingSlotAsync(PlaceOrderNormalSagaState saga, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var slots = scope.ServiceProvider.GetRequiredService<IPendingOrderSlotService>();
        if (Guid.TryParse(saga.UserId, out var uid))
            await slots.ReleaseAsync(uid, OrderType.Normal, ct);
    }

    private async Task PublishOrderConfirmedAsync(PlaceOrderNormalSagaState saga, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var repo      = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = await repo.GetByIdAsync(saga.OrderId, ct);
        if (order is null) return;

        await publisher.Publish(new OrderConfirmedV1(
            order.Id,
            order.OrderNumber,
            order.UserId,
            order.CustomerEmail,
            order.FinalAmount,
            DateTimeOffset.UtcNow,
            order.Items.Select(i =>
                new OrderItemSummary(i.ProductId, i.VariantId, i.VariantSku, i.Quantity, i.UnitPrice)).ToList()
        ), msgCtx =>
        {
            msgCtx.MessageId = DeterministicMessageId.From($"order-confirmed:{order.Id}");
        }, ct);
    }

    private async Task PublishOrderCancelledAsync(PlaceOrderNormalSagaState saga, string reason, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var repo      = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order       = await repo.GetByIdAsync(saga.OrderId, ct);
        var orderNumber = order?.OrderNumber ?? string.Empty;

        await publisher.Publish(new OrderIntegrationEvents.OrderCancelledV1(
            saga.OrderId,
            orderNumber,
            reason
        ), msgCtx =>
        {
            msgCtx.MessageId = DeterministicMessageId.From($"order-cancelled:{saga.OrderId}");
        }, ct);
    }

    // ── Snapshot helper ───────────────────────────────────────────────────────

    private static void SnapshotRequest(PlaceOrderNormalSagaState saga, PlaceOrderRequestedV1 msg)
    {
        saga.CorrelationId       = msg.OrderId;
        saga.OrderId             = msg.OrderId;
        saga.UserId              = msg.UserId;
        saga.IdempotencyKey      = msg.IdempotencyKey;
        saga.CouponCode          = msg.CouponCode;
        saga.Subtotal            = msg.Subtotal;
        saga.ShippingFee         = msg.ShippingFee;
        saga.ItemsJson           = JsonSerializer.Serialize(msg.Items);
        saga.ShippingAddressJson = msg.ShippingAddress is null
            ? null
            : JsonSerializer.Serialize(msg.ShippingAddress);
        saga.PricingSnapshotJson = msg.PricingSnapshotJson;
        saga.CustomerEmail       = msg.CustomerEmail;
        saga.CustomerName        = msg.CustomerName;
        saga.CustomerPhone       = msg.CustomerPhone;
        saga.CustomerNote        = msg.CustomerNote;
    }

    // ── Business validation ───────────────────────────────────────────────────

    // 1% tolerance matches existing OrderErrors.PriceMismatch documentation
    private const decimal PriceTolerancePercent = 0.01m;

    private static Result ValidateBusinessRules(
        IReadOnlyList<CatalogVariantInfo> variants,
        IReadOnlyList<NormalOrderItemSnapshot> items)
    {
        var variantMap = variants.ToDictionary(v => v.VariantId);

        foreach (var item in items)
        {
            if (!variantMap.TryGetValue(item.VariantId, out var variant))
                return Result.Failure(OrderErrors.VariantNotFound(item.VariantId));

            if (!variant.VariantIsActive)
                return Result.Failure(OrderErrors.VariantInactive(item.VariantId));

            if (!variant.ProductIsActive)
                return Result.Failure(OrderErrors.ProductInactive(variant.ProductId));

            if (!variant.SellerIsActive)
                return Result.Failure(OrderErrors.SellerInactive(variant.SellerId));

            var tolerance = variant.CurrentPrice * PriceTolerancePercent;
            if (Math.Abs(variant.CurrentPrice - item.UnitPrice) > tolerance)
                return Result.Failure(
                    OrderErrors.VariantPriceMismatch(item.VariantId, variant.CurrentPrice, item.UnitPrice));
        }

        return Result.Success();
    }

    // ── Message factories ─────────────────────────────────────────────────────

    private static decimal CalculateOrderTotal(PlaceOrderNormalSagaState saga) =>
        Math.Max(0m, saga.Subtotal - saga.CouponDiscount + saga.ShippingFee);

    private static ReserveInventoryRequestedV1 BuildInventoryRequest(PlaceOrderNormalSagaState saga)
    {
        var items = JsonSerializer.Deserialize<List<NormalOrderItemSnapshot>>(saga.ItemsJson!)!
            .Select(i => new InventoryReserveItem(i.ProductId, i.VariantId, i.Quantity))
            .ToList();

        return new ReserveInventoryRequestedV1
        {
            CorrelationId       = saga.OrderId.ToString("D"),
            OrderId             = saga.OrderId,
            OrderIdempotencyKey = $"{saga.IdempotencyKey}:inv",
            Items               = items
        };
    }

    private static ClaimCouponRequestedV1 BuildCouponRequest(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId       = saga.OrderId.ToString("D"),
        OrderId             = saga.OrderId,
        OrderIdempotencyKey = $"{saga.IdempotencyKey}:cpn",
        UserId              = saga.UserId,
        CouponCode          = saga.CouponCode!,
        OrderTotal          = CalculateOrderTotal(saga)
    };

    private static CreatePaymentSessionV1 BuildPaymentSessionRequest(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId  = saga.OrderId.ToString("D"),
        OrderId        = saga.OrderId,
        IdempotencyKey = $"{saga.IdempotencyKey}:pay",
        Amount         = CalculateOrderTotal(saga),
        Currency       = "VND"
    };

    private static InventoryReleaseRequestedV1 BuildInventoryRelease(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId = saga.OrderId.ToString("D"),
        ReservationId = saga.ReservationId!.Value,
        Reason        = saga.FailureReason ?? "Order failed"
    };

    private static CouponReleaseRequestedV1 BuildCouponRelease(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId = saga.OrderId.ToString("D"),
        ClaimId       = saga.CouponClaimId!.Value,
        Reason        = saga.FailureReason ?? "Order failed"
    };

    private static ConfirmInventoryRequestedV1 BuildConfirmInventoryRequest(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId  = saga.OrderId.ToString("D"),
        OrderId        = saga.OrderId,
        ReservationId  = saga.ReservationId!.Value,
        IdempotencyKey = $"{saga.IdempotencyKey}:confirm-inv"
    };
}
