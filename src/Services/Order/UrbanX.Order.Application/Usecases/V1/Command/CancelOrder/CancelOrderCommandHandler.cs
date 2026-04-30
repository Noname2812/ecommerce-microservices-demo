using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Order;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command;

public sealed class CancelOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext)
    : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure(OrderErrors.NotFound(request.OrderId));

        var userId = userContext.UserId!.Value;
        var isAdmin = userContext.HasRole(Shared.Application.Authorization.Roles.Admin);

        if (!isAdmin && !order.CanBeCancelledBy(userId))
            return Result.Failure(OrderErrors.Forbidden);

        if (order.Status == OrderStatus.Cancelled ||
            order.Status == OrderStatus.Completed ||
            order.Status == OrderStatus.Refunded)
            return Result.Failure(OrderErrors.CannotCancel);

        order.Cancel(request.Reason, userId, changedByName: string.Empty);

        await outboxWriter.WriteAsync(
            new OrderIntegrationEvents.OrderCancelledV1(order.Id, order.OrderNumber, request.Reason),
            cancellationToken);

        return Result.Success();
    }
}
