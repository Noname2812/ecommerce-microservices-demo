using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Order;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using UrbanX.Order.Infrastructure.Services;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command;

public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext,
    IPromotionServiceClient promotionClient,
    IProductValidator productValidator,
    IShippingValidator shippingValidator,
    IPricingValidator pricingValidator)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        // Idempotency check
        var existing = await orderRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return Result.Success(existing.Id);

        var currentUserId = userContext.UserId;
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        if (request.UserId != currentUserId.Value)
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

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var subTotal = request.Items.Sum(i => i.UnitPrice * i.Quantity);
            var redeemItems = request.Items
                .Select(i => new PromotionRedeemItemDto(i.VariantId, i.ProductId, i.Quantity, i.UnitPrice))
                .ToList();

            var promotionResult = await promotionClient.RedeemAsync(
                new PromotionRedeemRequest(null, userId, request.CouponCode, subTotal, redeemItems),
                cancellationToken);

            if (promotionResult.IsFailure)
                return Result.Failure<Guid>(OrderErrors.PromotionInvalid(promotionResult.Error.Message));

            var redeemed = promotionResult.Value!;
            couponDiscount = redeemed.OrderLevelDiscount;
            foreach (var d in redeemed.ItemDiscounts)
                itemDiscountMap[d.VariantId] = d.DiscountPerUnit;
        }

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
            itemDiscountMap.TryGetValue(i.VariantId, out var d) ? d * i.Quantity : i.DiscountAmount,
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

        orderRepository.Add(order);

        var itemSnapshots = order.Items
            .Select(i => new OrderItemSnapshot(
                i.ProductId, i.ProductName, i.VariantId, i.VariantSku, i.VariantName,
                i.SellerId, i.SellerName, i.Quantity, i.UnitPrice))
            .ToList();

        await outboxWriter.WriteAsync(new OrderIntegrationEvents.OrderCreatedV1(
            order.Id, order.OrderNumber, order.UserId,
            order.CustomerEmail, order.CustomerName,
            order.TotalAmount, itemSnapshots), cancellationToken);

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
        var suffix = Random.Shared.Next(1000, 9999);
        return $"ORD-{date}-{suffix}";
    }
}
