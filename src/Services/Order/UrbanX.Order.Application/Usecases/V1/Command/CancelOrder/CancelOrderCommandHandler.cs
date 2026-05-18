using MassTransit;
using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Order;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using Shared.Messaging;
using UrbanX.Order.Domain.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command;

public sealed class CancelOrderCommandHandler(
    IOrderRepository orderRepository,
    IPublishEndpoint publishEndpoint,
    IUserContext userContext)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order is null)
            return Result.Failure(OrderErrors.NotFound(request.OrderId));

        var userId = userContext.UserId!.Value;
        var isAdmin = userContext.HasRole(Roles.Admin);

        if (!isAdmin && !order.CanBeCancelledBy(userId))
            return Result.Failure(OrderErrors.Forbidden);

        if (order.Status == OrderStatus.Cancelled
            || order.Status == OrderStatus.Completed
            || order.Status == OrderStatus.Refunded)
            return Result.Failure(OrderErrors.CannotCancel);

        var hadReservation = order.ReservationId.HasValue;
        var hadCoupon = order.CouponClaimId.HasValue;
        var reservationId = order.ReservationId;
        var claimId = order.CouponClaimId;

        order.Cancel(request.Reason, userId, changedByName: string.Empty);

        var correlationId = order.Id.ToString("D");

        await publishEndpoint.Publish(
            new OrderIntegrationEvents.OrderCancelledV1(order.Id, order.OrderNumber, request.Reason),
            ctx => ctx.MessageId = DeterministicMessageId.From($"order-cancelled:{order.Id}"),
            ct);

        if (hadReservation)
        {
            await publishEndpoint.Publish(
                new InventoryReleaseRequestedV1
                {
                    CorrelationId = correlationId,
                    ReservationId = reservationId!.Value,
                    Reason = $"Cancelled by user: {request.Reason}"
                },
                ctx => ctx.MessageId = DeterministicMessageId.From($"inv-release:{reservationId}"),
                ct);
        }

        if (hadCoupon)
        {
            await publishEndpoint.Publish(
                new CouponReleaseRequestedV1
                {
                    CorrelationId = correlationId,
                    ClaimId = claimId!.Value,
                    Reason = $"Cancelled by user: {request.Reason}"
                },
                ctx => ctx.MessageId = DeterministicMessageId.From($"coupon-release:{claimId}"),
                ct);
        }

        return Result.Success();
    }
}
