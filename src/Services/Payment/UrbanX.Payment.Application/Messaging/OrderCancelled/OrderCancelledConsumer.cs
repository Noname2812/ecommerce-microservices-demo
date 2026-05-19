using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Order;
using Shared.Messaging;
using UrbanX.Payment.Application.Usecases.V1.Command.CancelPayment;
using UrbanX.Payment.Application.Usecases.V1.Command.CreateRefund;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Messaging.OrderCancelled;

public sealed class OrderCancelledConsumer
    : IntegrationEventConsumerBase<OrderIntegrationEvents.OrderCancelledV1, OrderCancelledConsumer>
{
    private readonly IMediator _sender;
    private readonly IPaymentRepository _paymentRepository;

    public OrderCancelledConsumer(
        IMediator mediator,
        IPaymentRepository paymentRepository,
        ILogger<OrderCancelledConsumer> logger)
        : base(mediator, logger)
    {
        _sender = mediator;
        _paymentRepository = paymentRepository;
    }

    protected override async Task HandleAsync(OrderIntegrationEvents.OrderCancelledV1 @event, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByOrderIdAsync(@event.OrderId, cancellationToken);
        if (payment is null)
            return;

        if (payment.Status == PaymentStatus.Completed)
        {
            await _sender.Send(new CreateRefundCommand(@event.OrderId, payment.Amount, $"Order cancelled: {@event.Reason}"), cancellationToken);
        }
        else if (payment.Status is PaymentStatus.Pending or PaymentStatus.Processing)
        {
            await _sender.Send(new CancelPaymentCommand(payment.Id), cancellationToken);
        }
    }
}
