using MediatR;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Usecases.V1.Command.CancelPayment;
using UrbanX.Payment.Application.Usecases.V1.Command.CreateRefund;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleOrderCancelled;

internal sealed class HandleOrderCancelledCommandHandler(
    IPaymentRepository paymentRepository,
    ISender sender) : ICommandHandler<HandleOrderCancelledCommand>
{
    public async Task<Result> Handle(HandleOrderCancelledCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
            return Result.Success();

        if (payment.Status == PaymentStatus.Completed)
        {
            return await sender.Send(
                new CreateRefundCommand(request.OrderId, payment.Amount, $"Order cancelled: {request.Reason}"),
                cancellationToken);
        }

        if (payment.Status is PaymentStatus.Pending or PaymentStatus.Processing)
            return await sender.Send(new CancelPaymentCommand(payment.Id), cancellationToken);

        return Result.Success();
    }
}
