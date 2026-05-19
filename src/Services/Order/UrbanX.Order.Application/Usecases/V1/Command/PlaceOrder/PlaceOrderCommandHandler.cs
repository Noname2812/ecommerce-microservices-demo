using MassTransit;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Messaging;
using UrbanX.Order.Application.Services;
using UrbanX.Order.Application.Usecases.V1.Command.Common;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IPublishEndpoint publishEndpoint,
    IPendingOrderSlotService pendingSlots,
    IUserContext userContext)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var userId = userContext.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var existing = await orderRepository.GetByIdempotencyKeyAsync(cmd.IdempotencyKey, ct);
        if (existing is not null)
            return Result.Success(existing.Id);

        var slot = await pendingSlots.TryAcquireAsync(userId, OrderType.Normal, ct);
        if (slot.IsFailure)
            return Result.Failure<Guid>(slot.Error);

        var orderId = Guid.NewGuid();
        var orderNumber = OrderNumberGenerator.Generate("ORD");
        try
        {
            var order = OrderFactory.Build(
                cmd,
                userId,
                orderId,
                orderNumber);

            orderRepository.Add(order);

            await publishEndpoint.Publish(
                new PlaceOrderRequestedV1
                {
                    OrderId = orderId,
                    OrderNumber = orderNumber,
                    CorrelationId = orderId.ToString("D"),
                    UserId = userId.ToString("D"),
                    IdempotencyKey = cmd.IdempotencyKey,
                    CouponCode = cmd.CouponCode,
                    Subtotal = PlaceOrderEventMappings.SumLineTotal(cmd.Items),
                    ShippingFee = cmd.ShippingFee,
                    ShippingAddress = PlaceOrderEventMappings.MapShipping(cmd.ShippingAddress),
                    PricingSnapshotJson = PlaceOrderEventMappings.SerializePricingSnapshot(cmd.PricingSnapshot),
                    CustomerEmail = cmd.CustomerEmail?.Trim() ?? string.Empty,
                    CustomerName = cmd.ShippingAddress.FullName,
                    CustomerPhone = cmd.ShippingAddress.Phone,
                    CustomerNote = cmd.CustomerNote,
                    Items = PlaceOrderEventMappings.MapNormalItems(cmd.Items)
                },
                ctx => ctx.MessageId = DeterministicMessageId.From($"place-order-requested:{orderId}"),
                ct);
        }
        catch
        {
            await pendingSlots.ReleaseAsync(userId, OrderType.Normal, ct);
            throw;
        }

        return Result.Success(orderId);
    }
}
