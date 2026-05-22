using MassTransit;
using MediatR;
using Shared.Contract.Messaging.Order;
using UrbanX.Payment.Application.Usecases.V1.Command.HandleOrderCancelled;

namespace UrbanX.Payment.Infrastructure.Messaging.OrderCancelled;

public sealed class OrderCancelledConsumer(ISender sender) : IConsumer<OrderIntegrationEvents.OrderCancelledV1>
{
    public Task Consume(ConsumeContext<OrderIntegrationEvents.OrderCancelledV1> context)
    {
        var command = new HandleOrderCancelledCommand(
            OrderId: context.Message.OrderId,
            Reason: context.Message.Reason);

        return sender.Send(command, context.CancellationToken);
    }
}
