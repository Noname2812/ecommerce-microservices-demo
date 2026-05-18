using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command.Common;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;

public sealed class PlaceOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext,
    IShippingValidator shippingValidator)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var currentUserId = userContext.UserId;
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Result.Failure<Guid>(OrderErrors.Forbidden);

        var userId = currentUserId.Value;

        // TODO(TASK-07): product/pricing validation moves to saga + ICatalogServiceClient
        var validation = await shippingValidator.ValidateAsync(cmd.ShippingAddress, ct);
        if (validation.IsFailure)
            return Result.Failure<Guid>(validation.Error);

        var order = OrderFactory.Build(cmd, userId, OrderNumberGenerator.Generate("ORD"));

        orderRepository.Add(order);

        await outboxWriter.WriteAsync(new PlaceOrderRequestedV1
        {
            OrderId        = order.Id,
            UserId         = userId.ToString("D"),
            IdempotencyKey = cmd.IdempotencyKey,
            CouponCode     = cmd.CouponCode,
            Subtotal       = order.Subtotal,
            ShippingFee    = order.ShippingFee,
            Items          = order.Items
                .Select(i => new NormalOrderItemSnapshot(i.ProductId, i.VariantId, i.Quantity, i.UnitPrice))
                .ToList()
        }, ct);

        return Result.Success(order.Id);
    }
}
