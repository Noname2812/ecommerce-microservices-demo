using Shared.Application;
using Shared.Application.Authorization;
using Shared.Cache.Abstractions;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using UrbanX.Order.Infrastructure.Exceptions;
using UrbanX.Order.Infrastructure.Services;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

public sealed class PlaceSalesOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    ICompensationOutboxWriter compensationOutboxWriter,
    IUserContext userContext,
    IInventoryClient inventoryClient,
    ICouponClient couponClient,
    IPromotionServiceClient promotionClient,
    IProductValidator productValidator,
    IShippingValidator shippingValidator,
    ISaleEligibilityValidator eligibilityValidator,
    ISaleAllocationGate allocationGate,
    ISalePricingValidator salePricingValidator,
    ICacheService cache,
    PlaceOrderCompensationContext orderCompensationContext,
    PlaceSalesOrderCompensationContext salesCompensationContext)
    : ICommandHandler<PlaceSalesOrderCommand, Guid>
{
    private static readonly TimeSpan IdempotencyGuardTtl = TimeSpan.FromHours(24);
    public async Task<Result<Guid>> Handle(PlaceSalesOrderCommand request, CancellationToken ct)
    {
        var currentUserId = userContext.UserId;
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var userId = currentUserId.Value;

        var guardKey = PlaceSalesOrderIdempotencyCacheKeys.GuardKey(request.IdempotencyKey);
        try
        {
            var cachedId = await cache.GetAsync<string>(guardKey, ct);
            if (!string.IsNullOrEmpty(cachedId) &&
                Guid.TryParse(cachedId, out var existingOrderId) &&
                existingOrderId != Guid.Empty)
                return Result.Success(existingOrderId);
        }
        catch
        {
            // Fail-open: Redis unavailable — continue (pipeline idempotency may still apply).
        }

        var eligibility = await eligibilityValidator.ValidateAsync(request.CampaignId, userId, request.Items, ct);
        if (eligibility.IsFailure)
            return Result.Failure<Guid>(eligibility.Error);

        var totalQty = request.Items.Sum(i => i.Quantity);
        var quotaResult = await allocationGate.TryReserveAsync(request.CampaignId, userId, totalQty, ct);
        if (quotaResult.IsFailure)
            return Result.Failure<Guid>(quotaResult.Error);

        salesCompensationContext.SaleQuotaKey     = quotaResult.Value;
        salesCompensationContext.SaleCampaignId   = request.CampaignId;
        salesCompensationContext.SaleUserId       = userId;
        salesCompensationContext.SaleReservedQty = totalQty;

        var validationResult = await ValidateBusinessRulesAsync(request, productValidator, shippingValidator, salePricingValidator, ct);
        if (validationResult.IsFailure)
            return Result.Failure<Guid>(validationResult.Error);

        decimal couponDiscount = 0;
        var itemDiscountMap = new Dictionary<Guid, decimal>();
        var subTotal = request.Items.Sum(i => i.UnitPrice * i.Quantity);

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var redeemItems = request.Items
                .Select(i => new PromotionRedeemItemDto(i.VariantId, i.ProductId, i.Quantity, i.UnitPrice))
                .ToList();

            var promotionResult = await promotionClient.RedeemAsync(
                new PromotionRedeemRequest(OrderId: null, userId, request.CouponCode, subTotal, redeemItems), ct);

            if (promotionResult.IsFailure)
                return Result.Failure<Guid>(OrderErrors.PromotionInvalid(promotionResult.Error.Message));

            var redeemed = promotionResult.Value!;
            couponDiscount = redeemed.OrderLevelDiscount;
            foreach (var d in redeemed.ItemDiscounts)
                itemDiscountMap[d.VariantId] = d.DiscountPerUnit;
        }

        Guid reservationId;
        try
        {
            var reserveItems = request.Items
                .Select(i => new ReserveLineItem(i.ProductId, i.Quantity))
                .ToList();

            var reservation = await inventoryClient.ReserveAsync(
                new ReserveRequest(request.IdempotencyKey, reserveItems), ct);

            reservationId = reservation.ReservationId;
            orderCompensationContext.ReservationId = reservationId;
        }
        catch (OutOfStockException ex)           { return Result.Failure<Guid>(OrderErrors.OutOfStock(ex.Message)); }
        catch (InventoryUnavailableException ex) { return Result.Failure<Guid>(OrderErrors.InventoryUnavailable(ex.Message)); }
        catch (HttpRequestException ex)          { return Result.Failure<Guid>(OrderErrors.InventoryUnavailable(ex.Message)); }

        Guid? couponClaimId = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            try
            {
                var claimResponse = await couponClient.ClaimAsync(
                    new ClaimCouponRequest(request.IdempotencyKey, request.CouponCode!, userId, subTotal),
                    new CouponClaimReservationContext(reservationId, compensationOutboxWriter),
                    ct);

                couponClaimId = claimResponse.ClaimId;
                orderCompensationContext.CouponClaimId = couponClaimId;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return Result.Failure<Guid>(OrderErrors.CouponClaimFailed(ex.Message)); }
        }

        var orderNumber = GenerateOrderNumber();
        var address = ShippingAddress.Create(
            request.ShippingAddress.Address, request.ShippingAddress.Ward,
            request.ShippingAddress.District, request.ShippingAddress.City,
            request.ShippingAddress.Province, request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode, request.ShippingAddress.FullName,
            request.ShippingAddress.Phone);

        var specs = request.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity,
            itemDiscountMap.TryGetValue(i.VariantId, out var d) ? d * i.Quantity : 0m,
            i.ImageUrl)).ToList();

        var order = OrderEntity.Create(
            orderNumber, userId,
            customerEmail: request.CustomerEmail?.Trim() ?? string.Empty,
            customerName: request.ShippingAddress.FullName,
            customerPhone: request.ShippingAddress.Phone,
            address, request.ShippingFee, request.CouponCode, couponDiscount,
            request.CustomerNote, request.IdempotencyKey, specs,
            orderType: OrderType.Sales,
            campaignId: request.CampaignId);

        order.SetConfirmedAsSalesOrder(
            reservationId, couponClaimId,
            request.CampaignId,
            userId, request.ShippingAddress.FullName);

        orderRepository.Add(order);

        await outboxWriter.WriteAsync(new PlaceSalesOrderConfirmedV1
        {
            OrderId       = order.Id,
            UserId        = order.UserId,
            CampaignId    = request.CampaignId,
            ReservationId = reservationId,
            ClaimId       = couponClaimId,
            FinalAmount   = order.FinalAmount,
            ConfirmedAt   = order.UpdatedAt
        }, CancellationToken.None);

        try
        {
            await cache.SetAsync(guardKey, order.Id.ToString("D"), IdempotencyGuardTtl, CancellationToken.None);
        }
        catch
        {
            // Best-effort; MediatR idempotency may still persist after commit.
        }

        return Result.Success(order.Id);
    }

    private static async Task<Result> ValidateBusinessRulesAsync(
        PlaceSalesOrderCommand request,
        IProductValidator productValidator,
        IShippingValidator shippingValidator,
        ISalePricingValidator salePricingValidator,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = new List<Task<Result>>
        {
            productValidator.ValidateAsync(request.Items, cts.Token),
            shippingValidator.ValidateAsync(request.ShippingAddress, cts.Token),
            salePricingValidator.ValidateAsync(request.CampaignId, request.PricingSnapshot, request.Items, cts.Token)
        };

        while (running.Count > 0)
        {
            var completed = await Task.WhenAny(running);
            running.Remove(completed);
            var result = await completed;
            if (result.IsFailure)
            {
                cts.Cancel();
                _ = Task.WhenAll(running).ContinueWith(
                    _ => { }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                return result;
            }
        }
        return Result.Success();
    }

    private static string GenerateOrderNumber()
    {
        var date   = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"SALE-{date}-{suffix}";
    }
}
