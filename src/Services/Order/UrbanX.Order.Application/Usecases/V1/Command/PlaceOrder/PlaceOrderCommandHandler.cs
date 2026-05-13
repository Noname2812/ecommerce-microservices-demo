using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Exceptions;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    ICompensationOutboxWriter compensationOutboxWriter,
    IUserContext userContext,
    IInventoryClient inventoryClient,
    ICouponClient couponClient,
    IPromotionServiceClient promotionClient,
    IProductValidator productValidator,
    IShippingValidator shippingValidator,
    IPricingValidator pricingValidator,
    PlaceOrderCompensationContext compensationContext)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = userContext.UserId;
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var userId = currentUserId.Value;

        var validationResult = await ValidateBusinessRulesAsync(
            request,
            productValidator,
            shippingValidator,
            pricingValidator,
            cancellationToken);
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

            Result<PromotionRedeemResponse> promotionResult;
            try
            {
                promotionResult = await promotionClient.RedeemAsync(
                    new PromotionRedeemRequest(null, userId, request.CouponCode, subTotal, redeemItems),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return Result.Failure<Guid>(OrderErrors.PromotionInvalid(ex.Message));
            }

            if (promotionResult.IsFailure)
                return Result.Failure<Guid>(OrderErrors.PromotionInvalid(promotionResult.Error.Message));

            var redeemed = promotionResult.Value!;
            couponDiscount = redeemed.OrderLevelDiscount;
            foreach (var d in redeemed.ItemDiscounts)
                itemDiscountMap[d.VariantId] = d.DiscountPerUnit;
        }

        // Step 4: Reserve inventory
        Guid reservationId;
        try
        {
            var reserveItems = request.Items
                .Select(i => new ReserveLineItem(i.ProductId, i.Quantity))
                .ToList();

            var reservation = await inventoryClient.ReserveAsync(
                new ReserveRequest(request.IdempotencyKey, reserveItems),
                cancellationToken);

            reservationId = reservation.ReservationId;
            compensationContext.ReservationId = reservationId;
        }
        catch (OutOfStockException ex)
        {
            return Result.Failure<Guid>(OrderErrors.OutOfStock(ex.Message));
        }
        catch (InventoryUnavailableException ex)
        {
            return Result.Failure<Guid>(OrderErrors.InventoryUnavailable(ex.Message));
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<Guid>(OrderErrors.InventoryUnavailable(ex.Message));
        }

        // Step 5: Claim coupon (only if present)
        Guid? couponClaimId = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            try
            {
                var claimResponse = await couponClient.ClaimAsync(
                    new ClaimCouponRequest(request.IdempotencyKey, request.CouponCode!, userId, subTotal),
                    new CouponClaimReservationContext(reservationId, compensationOutboxWriter),
                    cancellationToken);

                couponClaimId = claimResponse.ClaimId;
                compensationContext.CouponClaimId = couponClaimId;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result.Failure<Guid>(OrderErrors.CouponClaimFailed(ex.Message));
            }
        }

        // Step 6: Build and save order as CONFIRMED
        var orderNumber = GenerateOrderNumber();
        var address = ShippingAddress.Create(
            request.ShippingAddress.Address, request.ShippingAddress.Ward, request.ShippingAddress.District,
            request.ShippingAddress.City, request.ShippingAddress.Province, request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode, request.ShippingAddress.FullName, request.ShippingAddress.Phone);

        var specs = request.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity,
            itemDiscountMap.TryGetValue(i.VariantId, out var d) ? d * i.Quantity : 0m,
            i.ImageUrl
        )).ToList();

        var order = OrderEntity.Create(
            orderNumber,
            userId,
            customerEmail: request.CustomerEmail?.Trim() ?? string.Empty,
            customerName: request.ShippingAddress.FullName,
            customerPhone: request.ShippingAddress.Phone,
            address,
            request.ShippingFee,
            request.CouponCode,
            couponDiscount,
            request.CustomerNote,
            request.IdempotencyKey,
            specs);

        order.SetConfirmedWithReservation(reservationId, couponClaimId, userId, request.ShippingAddress.FullName);

        orderRepository.Add(order);

        await outboxWriter.WriteAsync(new OrderConfirmedForPlaceOrderV1
        {
            OrderId = order.Id,
            UserId = order.UserId,
            ReservationId = reservationId,
            ClaimId = couponClaimId,
            FinalAmount = order.FinalAmount,
            ConfirmedAt = order.UpdatedAt
        }, CancellationToken.None);

        return Result.Success(order.Id);
    }

    private static async Task<Result> ValidateBusinessRulesAsync(
        PlaceOrderCommand request,
        IProductValidator productValidator,
        IShippingValidator shippingValidator,
        IPricingValidator pricingValidator,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new List<Task<Result>>
        {
            productValidator.ValidateAsync(request.Items, cts.Token),
            shippingValidator.ValidateAsync(request.ShippingAddress, cts.Token),
            pricingValidator.ValidateAsync(request.PricingSnapshot, request.Items, cts.Token)
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
                    _ => { },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                return result;
            }
        }

        return Result.Success();
    }

    private static string GenerateOrderNumber()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"ORD-{date}-{suffix}";
    }
}
