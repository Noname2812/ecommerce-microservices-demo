using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CancelPayment;

public sealed class CancelPaymentCommandHandler(IPaymentRepository paymentRepository) : ICommandHandler<CancelPaymentCommand>
{
    public async Task<Result> Handle(CancelPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure(PaymentErrors.PaymentNotFound);

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.PartiallyPaid))
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        payment.MarkCancelled();
        return Result.Success();
    }
}
