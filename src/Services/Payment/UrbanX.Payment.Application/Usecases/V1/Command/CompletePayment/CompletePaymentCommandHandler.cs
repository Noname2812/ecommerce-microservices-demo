using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using Shared.Outbox.Abstractions;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CompletePayment;

public sealed class CompletePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IOutboxWriter outboxWriter) : ICommandHandler<CompletePaymentCommand>
{
    public async Task<Result> Handle(CompletePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure(PaymentErrors.PaymentNotFound);

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        payment.MarkCompleted(request.ProviderTransactionId);

        var integrationEvent = new PaymentIntegrationEvents.PaymentCompletedV1(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.Amount,
            payment.Currency,
            payment.ProviderName,
            payment.ProviderTransactionId,
            payment.PaidAt!.Value);

        await outboxWriter.WriteAsync(integrationEvent, cancellationToken);

        return Result.Success();
    }
}
