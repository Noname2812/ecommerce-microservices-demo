using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CompleteRefund;

public sealed class CompleteRefundCommandHandler(
    IRefundRepository refundRepository,
    IEventPublisher eventPublisher) : ICommandHandler<CompleteRefundCommand>
{
    public async Task<Result> Handle(CompleteRefundCommand request, CancellationToken cancellationToken)
    {
        var refund = await refundRepository.GetByIdAsync(request.RefundId, cancellationToken);
        if (refund is null)
            return Result.Failure(PaymentErrors.RefundNotFound);

        if (refund.Status != RefundStatus.Pending)
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        refund.MarkCompleted(request.ProviderRefundId);

        var integrationEvent = new PaymentIntegrationEvents.RefundProcessedV1(
            refund.Id,
            refund.PaymentId,
            refund.OrderId,
            refund.Amount,
            refund.Payment?.Currency ?? "VND",
            refund.ProviderRefundId,
            refund.ProcessedAt!.Value);

        await eventPublisher.PublishAsync(integrationEvent, cancellationToken);

        return Result.Success();
    }
}
