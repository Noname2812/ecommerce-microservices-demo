using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CreateRefund;

public sealed class CreateRefundCommandHandler(
    IPaymentRepository paymentRepository,
    IRefundRepository refundRepository) : ICommandHandler<CreateRefundCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRefundCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
            return Result.Failure<Guid>(PaymentErrors.PaymentNotFound);

        if (payment.Status != PaymentStatus.Completed)
            return Result.Failure<Guid>(PaymentErrors.PaymentNotCompleted);

        var refund = new Refund
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            OrderId = request.OrderId,
            Amount = request.Amount,
            Reason = request.Reason,
        };

        await refundRepository.AddAsync(refund, cancellationToken);

        return Result.Success(refund.Id);
    }
}
