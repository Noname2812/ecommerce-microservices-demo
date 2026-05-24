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
using UrbanX.Order.Application.Sagas.PlaceOrderNormal;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Order.Infrastructure.Sagas.PlaceOrderNormal;

/// <summary>
/// Orchestrates the place-normal-order flow:
///   Initial → ValidateCatalog → ResolveHold (if CouponHoldToken present) → InventoryReserving
///   → PaymentSessionCreating → PaymentPending → Finalized (+ fire-and-forget ClaimCouponRequested)
///   On failure → Compensating (releases inventory + Cart-hold) → Faulted
/// </summary>
public sealed class PlaceOrderNormalSagaStateMachine
    : SagaStateMachineBase<PlaceOrderNormalSagaState>
{
    // ── Workflow states ───────────────────────────────────────────────────────
    public State InventoryReserving     { get; private set; } = default!;
    public State PaymentSessionCreating { get; private set; } = default!;
    public State PaymentPending         { get; private set; } = default!;

    // ── Events ────────────────────────────────────────────────────────────────
    public Event<PlaceOrderRequestedV1>     Requested              { get; private set; } = default!;
    public Event<InventoryReservedV1>       InventoryReserved      { get; private set; } = default!;
    public Event<InventoryReserveFailedV1>  InventoryReserveFailed { get; private set; } = default!;
    public Event<PaymentSessionCreatedV1>   PaymentSessionCreated  { get; private set; } = default!;
    public Event<PaymentSessionCompletedV1> PaymentCompleted       { get; private set; } = default!;

    // ── Schedules ─────────────────────────────────────────────────────────────
    public Schedule<PlaceOrderNormalSagaState, InventoryStepTimeoutV1> InventoryTimeout { get; private set; } = default!;
    public Schedule<PlaceOrderNormalSagaState, PaymentSessionStepTimeoutV1> PaymentSessionTimeout { get; private set; } = default!;
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
                .ThenAsync(ctx => ResolveCouponHoldAsync(ctx))
                .IfElse(
                    ctx => ctx.Saga.ValidationError != null,
                    fail => fail
                        .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, ctx.Saga.ValidationError!, ctx.CancellationToken))
                        .ThenAsync(ctx => PublishOrderCancelledAsync(ctx.Saga, ctx.Saga.ValidationError!, ctx.CancellationToken))
                        .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                        .TransitionTo(Faulted),
                    ok => ok
                        .Schedule(InventoryTimeout, ctx => new InventoryStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(BuildInventoryRequest(ctx.Saga)))
                        .TransitionTo(InventoryReserving)));

        // ── InventoryReserving ─────────────────────────────────────────────────
        // Coupon is already resolved + reserved at the Initial step (Cart hold). No saga-level
        // coupon claim — claim is published fire-and-forget AFTER payment success.
        During(InventoryReserving,
            When(InventoryReserved)
                .Then(ctx => { ctx.Saga.ReservationIds = ctx.Message.ReservationIds; StampInstance(ctx.Saga); })
                .Unschedule(InventoryTimeout)
                .Schedule(PaymentSessionTimeout, ctx => new PaymentSessionStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(BuildPaymentSessionRequest(ctx.Saga)))
                .TransitionTo(PaymentSessionCreating),

            When(InventoryReserveFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "InventoryReserve";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(InventoryTimeout)
                .TransitionTo(Compensating),

            When(InventoryTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "InventoryTimeout";
                    ctx.Saga.FailureReason = "Inventory timeout";
                    StampInstance(ctx.Saga);
                })
                .TransitionTo(Compensating));

        During(PaymentSessionCreating,
            When(PaymentSessionCreated)
                .Unschedule(PaymentSessionTimeout)
                .ThenAsync(ctx => MarkReadyForPaymentAsync(ctx))
                .IfElse(
                    // MarkReadyForPaymentAsync sets FailureStep when order not found or UserId invalid
                    ctx => ctx.Saga.FailureStep != null,
                    fail => fail
                        .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                        .ThenAsync(ctx => ReleaseCouponHoldIfAnyAsync(ctx.Saga, ctx.CancellationToken))
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

            When(PaymentSessionTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep   = "PaymentSessionTimeout";
                    ctx.Saga.FailureReason = "Payment session create timeout";
                    StampInstance(ctx.Saga);
                })
                .PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga)))
                .ThenAsync(ctx => ReleaseCouponHoldIfAnyAsync(ctx.Saga, ctx.CancellationToken))
                .TransitionTo(Compensating));

        // ── PaymentPending ─────────────────────────────────────────────────────
        // On success: ClaimCouponRequested is published fire-and-forget — Promotion claim happens
        // OFF the order critical path. If Promotion processing fails, MT outbox retries; the customer
        // already paid the discounted price (saga.CouponDiscount), so a claim-record gap is back-office only.
        During(PaymentPending,
            When(PaymentCompleted)
                .Unschedule(PaymentExpiry)
                .ThenAsync(ctx => MarkOrderPaidAsync(ctx))
                .PublishAsync(ctx => ctx.Init<ConfirmInventoryRequestedV1>(BuildConfirmInventoryRequest(ctx.Saga)))
                .If(ctx => ctx.Saga.CouponHoldToken != null,
                    b => b.PublishAsync(ctx => ctx.Init<ClaimCouponRequestedV1>(BuildCouponRequest(ctx.Saga))))
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
                .ThenAsync(ctx => ReleaseCouponHoldIfAnyAsync(ctx.Saga, ctx.CancellationToken))
                .ThenAsync(ctx => CancelOrderAsync(ctx.Saga, "Payment expired", ctx.CancellationToken))
                .ThenAsync(ctx => PublishOrderCancelledAsync(ctx.Saga, "Payment expired", ctx.CancellationToken))
                .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                .TransitionTo(Faulted));

        // ── Compensating ─────────────────────────────────────────────────────
        // PaymentSessionTimeout already published InventoryReleaseRequestedV1 + released hold inline —
        // skip those branches here to avoid double-release.
        WhenEnter(Compensating, b => b
            .If(ctx => ctx.Saga.ReservationIds.Any()
                       && ctx.Saga.FailureStep != "PaymentSessionTimeout",
                x => x.PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga))))
            .If(ctx => ctx.Saga.CouponHoldToken != null
                       && ctx.Saga.FailureStep != "PaymentSessionTimeout",
                x => x.ThenAsync(ctx => ReleaseCouponHoldIfAnyAsync(ctx.Saga, ctx.CancellationToken)))
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
        Event(() => PaymentSessionCreated,  e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentCompleted,       e => e.CorrelateById(ctx => ctx.Message.OrderId));
    }

    private void ConfigureSchedules()
    {
        // Inventory reserve — internal service
        // Happy path ~200-500ms
        Schedule(() => InventoryTimeout, x => x.InventoryExpiryTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromSeconds(5);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        // Payment session — call external gateway (VNPay/Momo...)
        // Network round-trip + gateway processing
        Schedule(() => PaymentSessionTimeout, x => x.PaymentSessionExpiryTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromSeconds(10);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        // Payment expiry
        Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromMinutes(_paymentOptions.NormalOrderExpiryMinutes); // 15
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });
    }

    // ── Saga domain operations ────────────────────────────────────────────────

    /// <summary>
    /// Cart-issued hold token → coupon snapshot via cross-service Redis read. Sets <c>ValidationError</c>
    /// on failure so the existing Initial branch handles it. No-op when token is null.
    /// Also applies the discount to the Order entity (in-place) so DB pricing reflects the coupon.
    /// </summary>
    private async Task ResolveCouponHoldAsync(
        BehaviorContext<PlaceOrderNormalSagaState, PlaceOrderRequestedV1> ctx)
    {
        if (ctx.Saga.CouponHoldToken is null)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var holdClient = scope.ServiceProvider.GetRequiredService<ICouponHoldClient>();

        var hold = await holdClient.TryGetAsync(ctx.Saga.CouponHoldToken, ctx.CancellationToken);
        if (hold is null)
        {
            ctx.Saga.ValidationError = "COUPON_HOLD_EXPIRED";
            StampInstance(ctx.Saga);
            return;
        }

        // Guard against client tampering: hold must belong to the same user placing the order.
        if (Guid.TryParse(ctx.Saga.UserId, out var sagaUserId) && hold.UserId != sagaUserId)
        {
            ctx.Saga.ValidationError = "COUPON_HOLD_USER_MISMATCH";
            StampInstance(ctx.Saga);
            return;
        }

        ctx.Saga.CouponCode     = hold.CouponCode;
        ctx.Saga.CouponDiscount = hold.DiscountAmount;
        StampInstance(ctx.Saga);

        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(ctx.Saga.OrderId, ctx.CancellationToken);
            order?.ApplyCoupon(hold.CouponCode, hold.DiscountAmount);
        }, ctx.CancellationToken);
    }

    /// <summary>
    /// Best-effort hold release on order failure. The Cart hold reserved a user-lock + quota slot;
    /// compensation frees both so the user (or quota) is not stranded until TTL.
    /// </summary>
    private async Task ReleaseCouponHoldIfAnyAsync(PlaceOrderNormalSagaState saga, CancellationToken ct)
    {
        if (saga.CouponHoldToken is null)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var holdClient = scope.ServiceProvider.GetRequiredService<ICouponHoldClient>();
        await holdClient.ReleaseAsync(saga.CouponHoldToken, ct);
    }

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

        await publisher.Publish(new OrderIntegrationEvents.OrderCancelledV1(
            saga.OrderId,
            saga.OrderNumber,
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
        saga.OrderNumber         = msg.OrderNumber;
        saga.UserId              = msg.UserId;
        saga.IdempotencyKey      = msg.IdempotencyKey;
        saga.CouponHoldToken     = msg.CouponHoldToken;
        saga.CouponCode          = msg.CouponCode;  // resolved later if HoldToken is present
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
        saga.PaymentMethod       = msg.PaymentMethod;
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
            .Select(i => new InventoryReserveItem(i.VariantId, i.Quantity))
            .ToList();

        return new ReserveInventoryRequestedV1
        {
            CorrelationId       = saga.OrderId.ToString("D"),
            OrderId             = saga.OrderId,
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
        OrderTotal          = CalculateOrderTotal(saga),
        HoldToken           = saga.CouponHoldToken
    };

    private static CreatePaymentSessionV1 BuildPaymentSessionRequest(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId  = saga.OrderId.ToString("D"),
        OrderId        = saga.OrderId,
        OrderNumber    = saga.OrderNumber,
        IdempotencyKey = $"{saga.IdempotencyKey}:pay",
        Amount         = CalculateOrderTotal(saga),
        Currency       = "VND",
        CustomerId     = Guid.TryParse(saga.UserId, out var customerId) ? customerId : null,
        CustomerEmail  = saga.CustomerEmail,
        PaymentMethod  = saga.PaymentMethod
    };

    private static InventoryReleaseRequestedV1 BuildInventoryRelease(PlaceOrderNormalSagaState saga) => new()
    {
        CausationId     = saga.OrderId.ToString("D"),
        CorrelationId   = saga.OrderId.ToString("D"),
        OrderId         = saga.OrderId,
        Reason          = saga.FailureReason ?? "Order failed"
    };

    private static ConfirmInventoryRequestedV1 BuildConfirmInventoryRequest(PlaceOrderNormalSagaState saga) => new()
    {
        CorrelationId  = saga.OrderId.ToString("D"),
        OrderId        = saga.OrderId,
    };
}
