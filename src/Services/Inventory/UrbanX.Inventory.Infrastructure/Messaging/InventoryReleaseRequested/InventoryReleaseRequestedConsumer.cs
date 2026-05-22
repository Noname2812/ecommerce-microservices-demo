using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Inventory.Application.Usecases.V1.Command.Release;

namespace UrbanX.Inventory.Infrastructure.Messaging;

public sealed class InventoryReleaseRequestedConsumer : IConsumer<InventoryReleaseRequestedV1>
{
    private readonly ISender _sender;
    private readonly ILogger<InventoryReleaseRequestedConsumer> _logger;

    public InventoryReleaseRequestedConsumer(
        ISender sender,
        ILogger<InventoryReleaseRequestedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<InventoryReleaseRequestedV1> context)
    {
        var command = new ReleaseInventoryCommand(OrderId: context.Message.OrderId);

        await _sender.Send(command, context.CancellationToken);
    }
}
