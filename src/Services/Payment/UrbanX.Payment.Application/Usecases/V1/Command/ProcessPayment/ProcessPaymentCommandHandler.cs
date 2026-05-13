using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.ProcessPayment;

public sealed class ProcessPaymentCommandHandler(IPaymentRepository paymentRepository) : ICommandHandler<ProcessPaymentCommand>
{
    public async Task<Result> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure(PaymentErrors.PaymentNotFound);

        if (payment.Status != PaymentStatus.Pending)
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        payment.MarkProcessing();
        return Result.Success();
    }
}
