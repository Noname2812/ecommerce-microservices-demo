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
using UrbanX.Order.Application.Sagas.PlaceOrderSales;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Infrastructure.DependencyInjection.Options;
using SalesOrderItemSnapshot = Shared.Contract.Messaging.PlaceOrderSaga.OrderItemSnapshot;

namespace UrbanX.Order.Infrastructure.Sagas.PlaceOrderSales;

/// <summary>
/// Orchestrates the place-sales-order flow (TASK-08):
///   Initial (catalog + sale eligibility + coupon lock + persist order)
///     → InventoryReserving → PaymentSessionCreating → PaymentPending → Finalized
///   On failure → Compensating (restore flash-sale stock + release coupon + cancel) → Faulted
///
/// Server-side pricing recalc with configurable tolerance vs client ExpectedTotal.
/// Coupon uses Redis Lua atomic lock (not event-based). Flash sale stock was already
/// decremented by the handler; saga only restores on failure / timeout.
/// </summary>
public sealed class PlaceSalesOrderSagaStateMachine : SagaStateMachineBase<PlaceSalesOrderSagaState>
{
    // ── Workflow states ───────────────────────────────────────────────────────
    public State InventoryReserving { get; private set; } = default!;
    public State PaymentSessionCreating { get; private set; } = default!;
    public State PaymentPending { get; private set; } = default!;

    // ── Events ────────────────────────────────────────────────────────────────
    public Event<PlaceSalesOrderRequestedV1> Requested { get; private set; } = default!;
    public Event<InventoryReservedV1> InventoryReserved { get; private set; } = default!;
    public Event<InventoryReserveFailedV1> InventoryReserveFailed { get; private set; } = default!;
    public Event<PaymentSessionCreatedV1> PaymentSessionCreated { get; private set; } = default!;
    public Event<PaymentSessionCompletedV1> PaymentCompleted { get; private set; } = default!;

    // ── Schedules ─────────────────────────────────────────────────────────────
    public Schedule<PlaceSalesOrderSagaState, InventoryStepTimeoutV1> InventoryTimeout { get; private set; } = default!;
    public Schedule<PlaceSalesOrderSagaState, CouponStepTimeoutV1> CouponTimeout { get; private set; } = default!;
    public Schedule<PlaceSalesOrderSagaState, PaymentSessionStepTimeoutV1> PaymentSessionTimeout { get; private set; } = default!;
    public Schedule<PlaceSalesOrderSagaState, PaymentExpiryTimeoutV1> PaymentExpiry { get; private set; } = default!;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrderPaymentOptions _paymentOptions;
    private readonly decimal _priceTolerance;

    public PlaceSalesOrderSagaStateMachine(
        IOptions<OrderPaymentOptions> paymentOptions,
        IOptions<PlaceOrderOptions> placeOrderOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<PlaceSalesOrderSagaStateMachine> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _paymentOptions = paymentOptions.Value;
        _priceTolerance = placeOrderOptions.Value.PriceMismatchTolerance;

        InstanceState(x => x.CurrentState);

        ConfigureEvents();
        ConfigureSchedules();

        // ── Initially ─────────────────────────────────────────────────────────
        // One async method runs the full validation + persist pipeline. If any step sets
        // ValidationError the whole block short-circuits and we branch into compensation.
        Initially(
            When(Requested)
                .Then(ctx => SnapshotRequest(ctx.Saga, ctx.Message))
                .ThenAsync(ValidateAndPersistAsync)
                .IfElse(
                    ctx => ctx.Saga.ValidationError != null,
                    // Single compensation entry-point: `Compensating` handles every release path
                    // (coupon, flash sale stock, inventory, slot, optional order cancel) gated by
                    // saga flags. Avoids a parallel CompensateValidationFailureAsync that drifted
                    // from the post-persist path during code review.
                    fail => fail.TransitionTo(Compensating),
                    ok => ok
                        .Schedule(InventoryTimeout, ctx => new InventoryStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .PublishAsync(ctx => ctx.Init<ReserveInventoryRequestedV1>(BuildInventoryRequest(ctx.Saga)))
                        .TransitionTo(InventoryReserving)));

        // ── InventoryReserving ────────────────────────────────────────────────
        During(InventoryReserving,
            When(InventoryReserved)
                .Then(ctx =>
                {
                    // Sales orders carry a single reservation conceptually (one campaign per order),
                    // but the integration event returns the full list to stay consistent with the
                    // Normal flow. Take the first; treat empty as protocol violation.
                    ctx.Saga.ReservationId = ctx.Message.ReservationIds.Count > 0
                        ? ctx.Message.ReservationIds[0]
                        : throw new InvalidOperationException(
                            $"InventoryReservedV1 for order {ctx.Saga.OrderId} returned no reservation ids.");
                    StampInstance(ctx.Saga);
                })
                .Unschedule(InventoryTimeout)
                .TransitionTo(PaymentSessionCreating),

            When(InventoryReserveFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep = "InventoryReserve";
                    ctx.Saga.FailureReason = ctx.Message.ErrorMessage;
                    StampInstance(ctx.Saga);
                })
                .Unschedule(InventoryTimeout)
                .TransitionTo(Compensating),

            When(InventoryTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep = "InventoryTimeout";
                    ctx.Saga.FailureReason = "Inventory service did not respond within the allowed time.";
                    StampInstance(ctx.Saga);
                })
                .TransitionTo(Compensating));

        // ── PaymentSessionCreating ────────────────────────────────────────────
        WhenEnter(PaymentSessionCreating, b => b
            .Schedule(PaymentSessionTimeout, ctx => new PaymentSessionStepTimeoutV1 { OrderId = ctx.Saga.OrderId })
            .PublishAsync(ctx => ctx.Init<CreatePaymentSessionV1>(BuildPaymentSessionRequest(ctx.Saga))));

        During(PaymentSessionCreating,
            When(PaymentSessionCreated)
                .Unschedule(PaymentSessionTimeout)
                .ThenAsync(ctx => MarkReadyForPaymentAsync(ctx))
                .IfElse(
                    ctx => ctx.Saga.FailureStep != null,
                    fail => fail.TransitionTo(Compensating),
                    ok => ok
                        .Schedule(PaymentExpiry, ctx => new PaymentExpiryTimeoutV1 { OrderId = ctx.Saga.OrderId })
                        .TransitionTo(PaymentPending)),

            When(PaymentSessionTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep = "PaymentSessionTimeout";
                    ctx.Saga.FailureReason = "Payment session create timeout";
                    StampInstance(ctx.Saga);
                })
                .TransitionTo(Compensating));

        // ── PaymentPending ────────────────────────────────────────────────────
        During(PaymentPending,
            When(PaymentCompleted)
                .Unschedule(PaymentExpiry)
                .ThenAsync(ctx => MarkOrderPaidAsync(ctx))
                .ThenAsync(ctx => ConfirmCouponUseAsync(ctx.Saga, ctx.CancellationToken))
                .PublishAsync(ctx => ctx.Init<ConfirmInventoryRequestedV1>(BuildConfirmInventoryRequest(ctx.Saga)))
                .ThenAsync(ctx => PublishOrderConfirmedAsync(ctx.Saga, ctx.CancellationToken))
                .ThenAsync(ctx => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken))
                .Finalize(),

            When(PaymentExpiry.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStep = "PaymentExpiry";
                    ctx.Saga.FailureReason = "Payment expired";
                    StampInstance(ctx.Saga);
                })
                .TransitionTo(Compensating));

        // ── Compensating ─────────────────────────────────────────────────────
        // Releases every side-effect we may have acquired so far. Each release is gated by a saga
        // flag so the same path safely handles validation-phase failures (no ReservationId, no
        // persisted order) and post-persist failures (full cleanup).
        //
        // Order matters: publish inventory release first (durable via broker) — Redis ops run
        // afterwards on best-effort semantics inside their adapters (try/catch + log).
        WhenEnter(Compensating, b => b
            .If(ctx => ctx.Saga.ReservationId.HasValue,
                x => x.PublishAsync(ctx => ctx.Init<InventoryReleaseRequestedV1>(BuildInventoryRelease(ctx.Saga))))
            .ThenAsync(ctx => SafeCompensateAsync("ReleaseCoupon",
                () => ReleaseCouponLockAsync(ctx.Saga, ctx.CancellationToken)))
            .ThenAsync(ctx => SafeCompensateAsync("RestoreFlashSaleStock",
                () => RestoreFlashSaleStockAsync(ctx.Saga, ctx.CancellationToken)))
            .If(ctx => ctx.Saga.OrderPersisted,
                x => x
                    .ThenAsync(ctx => SafeCompensateAsync("CancelOrder",
                        () => CancelOrderAsync(ctx.Saga, FailureReasonOf(ctx.Saga), ctx.CancellationToken)))
                    .ThenAsync(ctx => SafeCompensateAsync("PublishOrderCancelled",
                        () => PublishOrderCancelledAsync(ctx.Saga, FailureReasonOf(ctx.Saga), ctx.CancellationToken))))
            .ThenAsync(ctx => SafeCompensateAsync("ReleasePendingSlot",
                () => ReleasePendingSlotAsync(ctx.Saga, ctx.CancellationToken)))
            .TransitionTo(Faulted));

        SetCompletedWhenFinalized();
        RegisterStateLogging();
    }

    // ── Event & schedule configuration ───────────────────────────────────────

    private void ConfigureEvents()
    {
        Event(() => Requested, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserved, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReserveFailed, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSessionCreated, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentCompleted, e => e.CorrelateById(ctx => ctx.Message.OrderId));
    }

    /// <remarks>
    /// MassTransit <c>Schedule(...)</c> evaluates <c>cfg.Delay</c> exactly once when the saga
    /// state machine is built. Because the state machine is a singleton, runtime changes to
    /// <c>OrderPaymentOptions.StepTimeoutSeconds</c> / <c>SalesOrderExpiryMinutes</c> via config
    /// reload will <em>not</em> take effect — restart the host to apply new timeout values.
    /// </remarks>
    private void ConfigureSchedules()
    {
        Schedule(() => InventoryTimeout, x => x.InventoryStepTimeoutTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromSeconds(_paymentOptions.StepTimeoutSeconds);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => CouponTimeout, x => x.CouponStepTimeoutTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromSeconds(_paymentOptions.StepTimeoutSeconds);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => PaymentSessionTimeout, x => x.PaymentSessionStepTimeoutTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromSeconds(_paymentOptions.StepTimeoutSeconds);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Schedule(() => PaymentExpiry, x => x.PaymentExpiryTokenId, cfg =>
        {
            cfg.Delay = TimeSpan.FromMinutes(_paymentOptions.SalesOrderExpiryMinutes);
            cfg.Received = r => r.CorrelateById(ctx => ctx.Message.OrderId);
        });
    }

    // ── Validation + persist pipeline ────────────────────────────────────────

    /// <summary>
    /// Runs each validation step in sequence; the first one to set <c>ValidationError</c>
    /// aborts the rest. Persists the Order at the end if everything passes.
    ///
    /// Idempotency runs first — before catalog/sale/coupon calls — so a duplicate
    /// IdempotencyKey doesn't waste an HTTP round-trip or acquire a redundant coupon lock.
    /// </summary>
    private async Task ValidateAndPersistAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PlaceSalesOrderRequestedV1> ctx)
    {
        if (!IsUserIdValid(ctx.Saga)) return;

        await CheckIdempotencyAsync(ctx);
        if (ctx.Saga.ValidationError != null) return;

        await ValidateThroughCatalogAsync(ctx);
        if (ctx.Saga.ValidationError != null) return;

        await ValidateSaleEligibilityAsync(ctx);
        if (ctx.Saga.ValidationError != null) return;

        await LockCouponAsync(ctx);
        if (ctx.Saga.ValidationError != null) return;

        ComputeFinalTotal(ctx.Saga);
        if (ctx.Saga.ValidationError != null) return;
    }

    private async Task CheckIdempotencyAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PlaceSalesOrderRequestedV1> ctx)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var existing = await repo.GetByIdempotencyKeyAsync(ctx.Saga.IdempotencyKey, ctx.CancellationToken);
        if (existing is null) return;

        Logger.LogWarning(
            "Sales order already exists for idempotency key {IdempotencyKey} as {ExistingOrderId}; aborting saga {OrderId}",
            ctx.Saga.IdempotencyKey, existing.Id, ctx.Saga.OrderId);

        ctx.Saga.ValidationError = "IDEMPOTENCY_CONFLICT";
        ctx.Saga.FailureStep = "Persist";
        ctx.Saga.FailureReason = $"Order already exists ({existing.Id})";
        StampInstance(ctx.Saga);
    }

    private async Task ValidateThroughCatalogAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PlaceSalesOrderRequestedV1> ctx)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogServiceClient>();

        var variantIds = ctx.Message.Items.Select(i => i.VariantId).Distinct().ToArray();
        var result = await catalog.GetVariantsAsync(variantIds, ctx.CancellationToken);

        if (result.IsFailure)
        {
            ctx.Saga.ValidationError = result.Error.Code == OrderErrors.CatalogUnavailable.Code
                ? "CATALOG_UNAVAILABLE"
                : "VARIANT_VALIDATION_FAILED";
            ctx.Saga.FailureStep = "CatalogValidation";
            ctx.Saga.FailureReason = result.Error.Message;
            StampInstance(ctx.Saga);
            return;
        }

        var validation = ValidateBusinessRules(result.Value!, ctx.Message.Items);
        if (validation.IsFailure)
        {
            ctx.Saga.ValidationError = validation.Error.Code;
            ctx.Saga.FailureStep = "CatalogValidation";
            ctx.Saga.FailureReason = validation.Error.Message;
            StampInstance(ctx.Saga);
            return;
        }

        ctx.Saga.VariantsJson = JsonSerializer.Serialize(result.Value);

        // Server-side subtotal authoritative; uses catalog current price * client quantity.
        var variantMap = result.Value!.ToDictionary(v => v.VariantId);
        ctx.Saga.Subtotal = ctx.Message.Items.Sum(i => variantMap[i.VariantId].CurrentPrice * i.Quantity);
        ctx.Saga.OriginalPrice = ctx.Saga.Subtotal;
        StampInstance(ctx.Saga);
    }

    private async Task ValidateSaleEligibilityAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PlaceSalesOrderRequestedV1> ctx)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sale = scope.ServiceProvider.GetRequiredService<ISaleEligibilityService>();

        var items = ctx.Message.Items
            .Select(i => new SaleEligibilityItem(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice))
            .ToList();

        var result = await sale.ValidateAsync(
            ctx.Saga.CampaignId, GetUserId(ctx.Saga), items, ctx.CancellationToken);

        if (result.IsFailure)
        {
            ctx.Saga.ValidationError = result.Error.Code;
            ctx.Saga.FailureStep = "SaleValidation";
            ctx.Saga.FailureReason = result.Error.Message;
            StampInstance(ctx.Saga);
            return;
        }

        ctx.Saga.SaleDiscount = result.Value!.SaleDiscountAmount;
        ctx.Saga.SaleStartAt = result.Value.StartAt;
        ctx.Saga.SaleEndAt = result.Value.EndAt;
        StampInstance(ctx.Saga);
    }

    private async Task LockCouponAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PlaceSalesOrderRequestedV1> ctx)
    {
        if (string.IsNullOrEmpty(ctx.Saga.CouponCode)) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var coupon = scope.ServiceProvider.GetRequiredService<ICouponLockService>();

        var result = await coupon.TryLockAsync(
            ctx.Saga.CouponCode!, GetUserId(ctx.Saga), ctx.CancellationToken);

        if (result.IsFailure)
        {
            ctx.Saga.ValidationError = result.Error.Code;
            ctx.Saga.FailureStep = "CouponLock";
            ctx.Saga.FailureReason = result.Error.Message;
            StampInstance(ctx.Saga);
            return;
        }

        ctx.Saga.CouponDiscount = result.Value!.DiscountAmount;
        ctx.Saga.CouponLocked = true;
        StampInstance(ctx.Saga);
    }

    private void ComputeFinalTotal(PlaceSalesOrderSagaState saga)
    {
        saga.FinalTotal = Math.Max(
            0m,
            saga.OriginalPrice - saga.SaleDiscount - saga.CouponDiscount + saga.ShippingFee);

        // Price mismatch tolerance — relative drift if FinalTotal > 0, otherwise reject when
        // ExpectedTotal is non-zero (a "free" order should match an expected total of 0).
        var mismatch = saga.FinalTotal > 0m
            ? Math.Abs(saga.ExpectedTotal - saga.FinalTotal) / saga.FinalTotal > _priceTolerance
            : saga.ExpectedTotal > 0m;

        if (mismatch)
        {
            saga.ValidationError = OrderErrors.PriceMismatch.Code;
            saga.FailureStep = "PriceMismatch";
            saga.FailureReason = $"Expected {saga.ExpectedTotal:F2}, server computed {saga.FinalTotal:F2}";
        }

        StampInstance(saga);
    }

    // ── Post-persist saga operations ─────────────────────────────────────────

    private async Task MarkReadyForPaymentAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PaymentSessionCreatedV1> ctx)
    {
        var userId = GetUserId(ctx.Saga);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Lambda is pure DB work — returns a bool to signal "found and updated".
        // Saga state mutation is deferred to *after* the (possibly retried) transaction commits,
        // so EF Core execution-strategy retries can't double-stamp inconsistent state.
        var updated = false;
        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(ctx.Saga.OrderId, ctx.CancellationToken);
            if (order is null) return;

            order.MarkReadyForPayment(
                claimId: null,                 // Sales flow uses Redis coupon lock, not event-based claim id
                ctx.Message.PaymentUrl,
                ctx.Message.QrCodeUrl,
                userId,
                string.Empty);
            updated = true;
        }, ctx.CancellationToken);

        if (!updated)
        {
            ctx.Saga.FailureStep = "OrderNotFound";
            ctx.Saga.FailureReason = $"Order {ctx.Saga.OrderId} not found during MarkReadyForPayment";
            StampInstance(ctx.Saga);
            return;
        }

        ctx.Saga.PaymentSessionId = ctx.Message.PaymentSessionId;
        ctx.Saga.PaymentUrl = ctx.Message.PaymentUrl;
        ctx.Saga.QrCodeUrl = ctx.Message.QrCodeUrl;
        ctx.Saga.PaymentExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_paymentOptions.SalesOrderExpiryMinutes);
        StampInstance(ctx.Saga);
    }

    private async Task MarkOrderPaidAsync(
        BehaviorContext<PlaceSalesOrderSagaState, PaymentSessionCompletedV1> ctx)
    {
        var userId = GetUserId(ctx.Saga);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(ctx.Saga.OrderId, ctx.CancellationToken);
            if (order is null) return;
            order.MarkPaid(ctx.Message.PaymentSessionId, userId, string.Empty);
        }, ctx.CancellationToken);
    }

    private async Task CancelOrderAsync(PlaceSalesOrderSagaState saga, string reason, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInTransactionAsync(async () =>
        {
            var order = await repo.GetByIdAsync(saga.OrderId, ct);
            if (order is null) return;
            order.Cancel(reason, GetUserId(saga), null);
        }, ct);
    }

    // ── Compensation helpers ──────────────────────────────────────────────────

    private async Task ReleaseCouponLockAsync(PlaceSalesOrderSagaState saga, CancellationToken ct)
    {
        if (!saga.CouponLocked || string.IsNullOrEmpty(saga.CouponCode)) return;
        if (!Guid.TryParse(saga.UserId, out var uid)) return;   // INVALID_USER_ID path

        await using var scope = _scopeFactory.CreateAsyncScope();
        var coupon = scope.ServiceProvider.GetRequiredService<ICouponLockService>();
        await coupon.ReleaseAsync(saga.CouponCode!, uid, ct);
        saga.CouponLocked = false;
        StampInstance(saga);
    }

    private async Task ConfirmCouponUseAsync(PlaceSalesOrderSagaState saga, CancellationToken ct)
    {
        if (!saga.CouponLocked || string.IsNullOrEmpty(saga.CouponCode)) return;
        if (!Guid.TryParse(saga.UserId, out var uid)) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var coupon = scope.ServiceProvider.GetRequiredService<ICouponLockService>();
        await coupon.ConfirmUseAsync(saga.CouponCode!, uid, ct);
        saga.CouponLocked = false;
        StampInstance(saga);
    }

    private async Task RestoreFlashSaleStockAsync(PlaceSalesOrderSagaState saga, CancellationToken ct)
    {
        var items = DeserializeItems(saga);
        if (items.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var stock = scope.ServiceProvider.GetRequiredService<IFlashSaleStockService>();
        var totalQty = items.Sum(i => i.Quantity);
        if (totalQty > 0)
            await stock.RestoreAsync(saga.CampaignId, totalQty, ct);
    }

    private async Task ReleasePendingSlotAsync(PlaceSalesOrderSagaState saga, CancellationToken ct = default)
    {
        // If UserId is malformed (INVALID_USER_ID path), skip — the handler-side slot has a TTL
        // that will clean up automatically; we have no key to release with.
        if (!Guid.TryParse(saga.UserId, out var uid)) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var slots = scope.ServiceProvider.GetRequiredService<IPendingOrderSlotService>();
        await slots.ReleaseAsync(uid, OrderType.Sales, ct);
    }

    private async Task PublishOrderConfirmedAsync(PlaceSalesOrderSagaState saga, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

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

    private async Task PublishOrderCancelledAsync(PlaceSalesOrderSagaState saga, string reason, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = await repo.GetByIdAsync(saga.OrderId, ct);
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

    // ── Snapshot + invariants ─────────────────────────────────────────────────

    /// <summary>
    /// Snapshots the inbound request onto the saga instance. Captures everything the saga needs
    /// for both happy and compensation paths. UserId validity is checked separately in
    /// <see cref="IsUserIdValid"/> so a malformed value flows through the normal validation-error
    /// path (Compensating restores flash sale stock + releases the pending slot) instead of
    /// throwing and triggering MassTransit's retry-to-DLQ loop on what is an unrecoverable input.
    /// </summary>
    private static void SnapshotRequest(PlaceSalesOrderSagaState saga, PlaceSalesOrderRequestedV1 msg)
    {
        saga.CorrelationId = msg.OrderId;
        saga.OrderId = msg.OrderId;
        saga.UserId = msg.UserId;
        saga.CampaignId = msg.CampaignId;
        saga.IdempotencyKey = msg.IdempotencyKey;
        saga.CouponCode = msg.CouponCode;
        saga.ExpectedTotal = msg.ExpectedTotal;
        saga.ShippingFee = msg.ShippingFee;
        saga.ItemsJson = JsonSerializer.Serialize(msg.Items);
        saga.ShippingAddressJson = JsonSerializer.Serialize(msg.ShippingAddress);
        saga.CustomerEmail = msg.CustomerEmail;
        saga.CustomerName = msg.CustomerName;
        saga.CustomerPhone = msg.CustomerPhone;
        saga.CustomerNote = msg.CustomerNote;
        saga.PaymentMethod = msg.PaymentMethod;
    }

    /// <summary>
    /// First-step gate inside <see cref="ValidateAndPersistAsync"/>: marks the saga with a
    /// <c>ValidationError</c> if the inbound UserId is unusable. Compensating still runs to
    /// restore the flash-sale stock the handler already decremented and to release the
    /// pending slot the handler already acquired.
    /// </summary>
    private bool IsUserIdValid(PlaceSalesOrderSagaState saga)
    {
        if (Guid.TryParse(saga.UserId, out _)) return true;

        saga.ValidationError = "INVALID_USER_ID";
        saga.FailureStep = "Snapshot";
        saga.FailureReason = $"UserId '{saga.UserId}' is not a valid Guid";
        StampInstance(saga);
        return false;
    }

    /// <summary>UserId is guaranteed valid once <see cref="IsUserIdValid"/> returned true.</summary>
    private static Guid GetUserId(PlaceSalesOrderSagaState saga) => Guid.Parse(saga.UserId);

    /// <summary>
    /// Best human-readable reason for compensation publish: prefer the per-step
    /// <c>FailureReason</c> (set by each validation step to <c>result.Error.Message</c>),
    /// fall back to the machine code, then a generic string.
    /// </summary>
    private static string FailureReasonOf(PlaceSalesOrderSagaState saga) =>
        saga.FailureReason ?? saga.ValidationError ?? "Order failed";

    private static List<SalesOrderItemSnapshot> DeserializeItems(PlaceSalesOrderSagaState saga)
    {
        if (string.IsNullOrEmpty(saga.ItemsJson))
            return [];

        return JsonSerializer.Deserialize<List<SalesOrderItemSnapshot>>(saga.ItemsJson)
            ?? throw new InvalidOperationException($"Saga {saga.OrderId} has invalid ItemsJson.");
    }

    // ── Business validation ───────────────────────────────────────────────────

    private static Result ValidateBusinessRules(
        IReadOnlyList<CatalogVariantInfo> variants,
        IReadOnlyList<SalesOrderItemSnapshot> items)
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
        }

        return Result.Success();
    }

    // ── Message factories ─────────────────────────────────────────────────────

    private static ReserveInventoryRequestedV1 BuildInventoryRequest(PlaceSalesOrderSagaState saga)
    {
        var items = DeserializeItems(saga)
            .Select(i => new InventoryReserveItem(i.VariantId, i.Quantity))
            .ToList();

        return new ReserveInventoryRequestedV1
        {
            CorrelationId = saga.OrderId.ToString("D"),
            OrderId = saga.OrderId,
            Items = items
        };
    }

    private static CreatePaymentSessionV1 BuildPaymentSessionRequest(PlaceSalesOrderSagaState saga) => new()
    {
        CorrelationId = saga.OrderId.ToString("D"),
        OrderId = saga.OrderId,
        IdempotencyKey = $"{saga.IdempotencyKey}:pay",
        Amount = saga.FinalTotal,
        Currency = "VND",
        CustomerId = Guid.TryParse(saga.UserId, out var customerId) ? customerId : null,
        CustomerEmail = saga.CustomerEmail,
        PaymentMethod = saga.PaymentMethod
    };

    private static InventoryReleaseRequestedV1 BuildInventoryRelease(PlaceSalesOrderSagaState saga) => new()
    {
        CorrelationId = saga.OrderId.ToString("D"),
        OrderId = saga.OrderId,
        Reason = FailureReasonOf(saga)
    };

    private static ConfirmInventoryRequestedV1 BuildConfirmInventoryRequest(PlaceSalesOrderSagaState saga) => new()
    {
        CorrelationId = saga.OrderId.ToString("D"),
        OrderId = saga.OrderId
    };
}
