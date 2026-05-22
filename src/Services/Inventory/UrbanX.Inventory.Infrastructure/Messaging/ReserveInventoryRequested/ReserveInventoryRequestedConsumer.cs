using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.PlaceOrderSaga;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

namespace UrbanX.Inventory.Infrastructure.Messaging;

public sealed class ReserveInventoryRequestedConsumer : IConsumer<ReserveInventoryRequestedV1>
{
    private readonly ISender _sender;
    private readonly ILogger<ReserveInventoryRequestedConsumer> _logger;
    public ReserveInventoryRequestedConsumer(
        ISender sender,
        ILogger<ReserveInventoryRequestedConsumer> logger
    )
    {
        _sender = sender;
        _logger = logger;
    }
    public async Task Consume(ConsumeContext<ReserveInventoryRequestedV1> context)
    {
        var command = new ReserveInventoryCommand(
                OrderId: context.Message.OrderId,
                ExpiresInMinutes: context.Message.ExpiresInMinutes,
                Items: context.Message.Items
                    .Select(i => new ReserveInventoryLineItem(i.VariantId, i.Quantity))
                    .ToList());
        
        await _sender.Send(command, context.CancellationToken);
    }
}

