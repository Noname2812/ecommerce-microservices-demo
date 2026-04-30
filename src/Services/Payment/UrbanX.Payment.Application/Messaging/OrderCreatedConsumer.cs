using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Order;
using Shared.Messaging;
using UrbanX.Payment.Application.Usecases.V1.Command.CreatePayment;

namespace UrbanX.Payment.Application.Messaging;

public sealed class OrderCreatedConsumer
    : IntegrationEventConsumerBase<OrderIntegrationEvents.OrderCreatedV1, OrderCreatedConsumer>
{
    private readonly IMediator _sender;

    public OrderCreatedConsumer(IMediator mediator, ILogger<OrderCreatedConsumer> logger)
        : base(mediator, logger)
    {
        _sender = mediator;
    }

    protected override async Task HandleAsync(OrderIntegrationEvents.OrderCreatedV1 @event, CancellationToken cancellationToken)
    {
        var command = new CreatePaymentCommand(
            @event.OrderId,
            @event.OrderNumber,
            @event.CustomerId,
            @event.CustomerEmail,
            @event.TotalAmount,
            IdempotencyKey: $"order-payment-{@event.OrderId}");

        await _sender.Send(command, cancellationToken);
    }
}
