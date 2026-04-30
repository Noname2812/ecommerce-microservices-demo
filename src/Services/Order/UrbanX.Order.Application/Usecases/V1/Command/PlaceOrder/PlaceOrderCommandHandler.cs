using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Order;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;
using UrbanX.Order.Domain.ValueObjects;
using OrderEntity = UrbanX.Order.Domain.Models.Order;

namespace UrbanX.Order.Application.Usecases.V1.Command;

public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext)
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
        var orderNumber = GenerateOrderNumber();
        var address = ShippingAddress.Create(
            request.Street, request.Ward, request.District,
            request.City, request.Province, request.Country,
            request.ZipCode, request.RecipientName, request.RecipientPhone);

        var specs = request.Items.Select(i => new NewOrderItemSpec(
            i.ProductId, i.ProductName, i.ProductSlug,
            i.VariantId, i.VariantSku, i.VariantName,
            i.SellerId, i.SellerName,
            i.UnitPrice, i.Quantity, i.DiscountAmount, i.ImageUrl
        )).ToList();

        var order = OrderEntity.Create(
            orderNumber,
            customerId,
            customerEmail: string.Empty,   // populated from user profile — placeholder
            customerName: string.Empty,     // populated from user profile — placeholder
            customerPhone: null,
            address,
            request.ShippingFee,
            request.CouponCode,
            request.CouponDiscount,
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
