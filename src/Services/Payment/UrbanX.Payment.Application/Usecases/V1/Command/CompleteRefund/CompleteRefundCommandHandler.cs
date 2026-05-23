using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.CompleteRefund;

public sealed class CompleteRefundCommandHandler(
    IRefundRepository refundRepository,
    IPaymentRepository paymentRepository,
    IEnumerable<IPaymentRefundProvider> refundProviders,
    IEventPublisher eventPublisher,
    ILogger<CompleteRefundCommandHandler> logger) : ICommandHandler<CompleteRefundCommand>
{
    public async Task<Result> Handle(CompleteRefundCommand request, CancellationToken cancellationToken)
    {
        var refund = await refundRepository.GetByIdAsync(request.RefundId, cancellationToken);
        if (refund is null)
            return Result.Failure(PaymentErrors.RefundNotFound);

        if (refund.Status != RefundStatus.Pending)
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        var payment = await paymentRepository.GetByIdAsync(refund.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure(PaymentErrors.PaymentNotFound);

        var providerMethod = MapProviderNameToMethod(payment.ProviderName);
        var providerRefundId = request.ProviderRefundId;

        var refundProvider = refundProviders.FirstOrDefault(p =>
            string.Equals(p.Method, providerMethod, StringComparison.OrdinalIgnoreCase));

        if (refundProvider is not null)
        {
            if (string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
            {
                logger.LogWarning(
                    "Cannot auto-refund payment {PaymentId} via {Provider}: ProviderTransactionId is missing.",
                    payment.Id, payment.ProviderName);
                return Result.Failure(PaymentErrors.RefundFailed);
            }

            var providerResult = await refundProvider.RefundAsync(
                refund.Id,
                payment.Id,
                payment.ProviderTransactionId!,
                refund.Amount,
                refund.Reason ?? "Refund",
                cancellationToken);

            if (providerResult.IsFailure)
            {
                refund.MarkFailed();
                return Result.Failure(providerResult.Error);
            }

            providerRefundId = providerResult.Value;
        }

        refund.MarkCompleted(providerRefundId);

        var integrationEvent = new PaymentIntegrationEvents.RefundProcessedV1(
            refund.Id,
            refund.PaymentId,
            refund.OrderId,
            refund.Amount,
            payment.Currency,
            refund.ProviderRefundId,
            refund.ProcessedAt!.Value);

        await eventPublisher.PublishAsync(integrationEvent, cancellationToken);

        return Result.Success();
    }

    // Payment.ProviderName stores friendly names ("SePay", "MoMo"); IPaymentRefundProvider.Method matches ProviderType constants.
    private static string MapProviderNameToMethod(string? providerName) => providerName switch
    {
        "MoMo" => ProviderType.Momo,
        "SePay" => ProviderType.Sepay,
        _ => providerName ?? string.Empty
    };
}
