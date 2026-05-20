using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.FailPayment;

public sealed class FailPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IEventPublisher eventPublisher) : ICommandHandler<FailPaymentCommand>
{
    public async Task<Result> Handle(FailPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure(PaymentErrors.PaymentNotFound);

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        payment.MarkFailed(request.FailureReason);

        var integrationEvent = new PaymentIntegrationEvents.PaymentFailedV1(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            request.FailureReason);

        await eventPublisher.PublishAsync(integrationEvent, cancellationToken);

        return Result.Success();
    }
}
