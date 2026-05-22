using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrderSaga;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Inventory.Infrastructure.Messaging;

public sealed class ConfirmInventoryRequestedConsumer : IConsumer<ConfirmInventoryRequestedV1>
{
    private readonly ISender _sender;
    private readonly ILogger<ConfirmInventoryRequestedConsumer> _logger;

    public ConfirmInventoryRequestedConsumer(
        ISender sender,
        ILogger<ConfirmInventoryRequestedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ConfirmInventoryRequestedV1> context)
    {
        var command = new ConfirmReservationCommand(OrderId: context.Message.OrderId);

        await _sender.Send(command, context.CancellationToken);
    }
}
