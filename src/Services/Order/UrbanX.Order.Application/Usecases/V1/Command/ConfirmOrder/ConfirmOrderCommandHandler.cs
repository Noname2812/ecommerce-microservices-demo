using Shared.Application;
using Shared.Application.Authorization;
using Shared.Contract.Messaging.Order;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Errors;
using UrbanX.Order.Domain.Models;
using UrbanX.Order.Domain.Repositories;

namespace UrbanX.Order.Application.Usecases.V1.Command;

public sealed class ConfirmOrderCommandHandler(
    IOrderRepository orderRepository,
    IOutboxWriter outboxWriter,
    IUserContext userContext)
    : ICommandHandler<ConfirmOrderCommand>
{
    public async Task<Result> Handle(ConfirmOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result.Failure(OrderErrors.NotFound(request.OrderId));

        if (order.Status != OrderStatus.Pending)
            return Result.Failure(OrderErrors.CannotCancel);

        var userId = userContext.UserId!.Value;
        order.Confirm(userId, changedByName: string.Empty);

        await outboxWriter.WriteAsync(
            new OrderIntegrationEvents.OrderConfirmedV1(order.Id, order.OrderNumber),
            cancellationToken);

        return Result.Success();
    }
}
