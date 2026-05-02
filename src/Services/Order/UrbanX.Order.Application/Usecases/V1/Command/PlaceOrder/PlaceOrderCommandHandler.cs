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
    IPromotionServiceClient promotionClient)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        // Idempotency check
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await orderRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return Result.Success(existing.Id);
        }

        var customerId = userContext.UserId!.Value;

        decimal couponDiscount = 0;
        var itemDiscountMap = new Dictionary<Guid, decimal>();

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var subTotal = request.Items.Sum(i => i.UnitPrice * i.Quantity);
            var redeemItems = request.Items
                .Select(i => new PromotionRedeemItemDto(i.VariantId, i.ProductId, i.Quantity, i.UnitPrice))
                .ToList();

            var promotionResult = await promotionClient.RedeemAsync(
                new PromotionRedeemRequest(null, customerId, request.CouponCode, subTotal, redeemItems),
                cancellationToken);

            if (promotionResult.IsFailure)
                return Result.Failure<Guid>(OrderErrors.PromotionInvalid(promotionResult.Error.Message));

            couponDiscount = promotionResult.Value.OrderLevelDiscount;
            foreach (var d in promotionResult.Value.ItemDiscounts)
                itemDiscountMap[d.VariantId] = d.DiscountPerUnit;
        }

        var orderNumber = GenerateOrderNumber();
        var address = ShippingAddress.Create(
            request.Street, request.Ward, request.District,
            request.City, request.Province, request.Country,
            request.ZipCode, request.RecipientName, request.RecipientPhone);

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
            customerId,
            customerEmail: string.Empty,
            customerName: string.Empty,
            customerPhone: null,
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
            order.Id, order.OrderNumber, order.CustomerId,
            order.CustomerEmail, order.CustomerName,
            order.TotalAmount, itemSnapshots), cancellationToken);

        return Result.Success(order.Id);
    }

    private static string GenerateOrderNumber()
    {
        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var suffix = Random.Shared.Next(1000, 9999);
        return $"ORD-{date}-{suffix}";
    }
}
