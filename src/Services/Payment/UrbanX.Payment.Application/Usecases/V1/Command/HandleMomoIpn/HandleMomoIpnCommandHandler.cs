using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Integrations.Momo;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleMomoIpn;

internal sealed class HandleMomoIpnCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentEventRepository paymentEventRepository,
    IAutoRefundService autoRefundService,
    IEventPublisher eventPublisher,
    ILogger<HandleMomoIpnCommandHandler> logger)
    : ICommandHandler<HandleMomoIpnCommand, MomoIpnResult>
{
    public async Task<Result<MomoIpnResult>> Handle(HandleMomoIpnCommand request, CancellationToken cancellationToken)
    {
        var externalTxId = request.TransId.ToString();

        if (await paymentEventRepository.ExistsByExternalTransactionIdAsync(externalTxId, cancellationToken))
        {
            logger.LogInformation(
                "MoMo IPN duplicate transId {TransId} for orderId {OrderId}; acknowledging.",
                request.TransId, request.OrderId);
            return Result.Success(new MomoIpnResult(true, "duplicate"));
        }

        var payment = await paymentRepository.GetByTransferReferenceAsync(request.OrderId, cancellationToken);
        if (payment is null)
        {
            logger.LogWarning(
                "MoMo IPN orderId {OrderId} not matched to any payment; acknowledging to stop retries.",
                request.OrderId);
            return Result.Success(new MomoIpnResult(true, "no match"));
        }

        if (payment.Status == PaymentStatus.Completed)
            return Result.Success(new MomoIpnResult(true, "already completed"));

        var isSuccessResult = request.ResultCode is
            MomoIntegrationConstants.ResultCodeSuccess or MomoIntegrationConstants.ResultCodeAuthorized;

        if (payment.Status == PaymentStatus.Expired)
        {
            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceivedAfterExpiry,
                request.RawPayloadJson,
                externalTxId,
                request.Amount,
                cancellationToken);

            // Funds arrived after the order saga gave up — refund full amount regardless of threshold.
            if (isSuccessResult && request.Amount > 0)
            {
                await autoRefundService.CreateAndAttemptAsync(
                    payment.Id,
                    request.Amount,
                    reason: "cancelled-but-paid:expired",
                    enforceThreshold: false,
                    cancellationToken);
            }
            return Result.Success(new MomoIpnResult(true, "expired"));
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            logger.LogWarning(
                "MoMo IPN arrived after payment {PaymentId} was cancelled — issuing auto-refund.",
                payment.Id);

            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceivedAfterCancellation,
                request.RawPayloadJson,
                externalTxId,
                request.Amount,
                cancellationToken);

            if (isSuccessResult && request.Amount > 0)
            {
                await autoRefundService.CreateAndAttemptAsync(
                    payment.Id,
                    request.Amount,
                    reason: "cancelled-but-paid:cancelled",
                    enforceThreshold: false,
                    cancellationToken);
            }
            return Result.Success(new MomoIpnResult(true, "cancelled-but-paid"));
        }

        var isPending = request.ResultCode is
            MomoIntegrationConstants.ResultCodeInitiated or
            MomoIntegrationConstants.ResultCodeProcessing or
            MomoIntegrationConstants.ResultCodeProcessingPay;

        if (isPending)
        {
            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceived,
                request.RawPayloadJson,
                externalTxId,
                request.Amount,
                cancellationToken);
            return Result.Success(new MomoIpnResult(true, "pending"));
        }

        if (!isSuccessResult)
        {
            var reason = $"momo:{request.ResultCode}:{request.Message}";
            payment.MarkFailed(reason);

            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceived,
                request.RawPayloadJson,
                externalTxId,
                request.Amount,
                cancellationToken);

            var failedEvent = new PaymentIntegrationEvents.PaymentFailedV1(
                payment.Id,
                payment.OrderId,
                payment.OrderNumber,
                payment.CustomerId,
                reason);

            await eventPublisher.PublishAsync(failedEvent, cancellationToken);
            return Result.Success(new MomoIpnResult(true, "failed"));
        }

        // Success path — MoMo enforces the fixed amount at /create, but guard against gateway anomalies.
        var expectedAmount = payment.Amount;
        var delta = request.Amount - expectedAmount;

        payment.MarkCompletedViaBankTransfer(request.Amount, request.RawPayloadJson, externalTxId);

        await RecordEventAsync(
            payment,
            PaymentEventTypes.WebhookReceived,
            request.RawPayloadJson,
            externalTxId,
            request.Amount,
            cancellationToken);

        var completedEvent = new PaymentIntegrationEvents.PaymentCompletedV1(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.Amount,
            payment.PaidAmount,
            payment.Currency,
            payment.ProviderName,
            payment.ProviderTransactionId,
            payment.PaidAt!.Value);

        await eventPublisher.PublishAsync(completedEvent, cancellationToken);

        if (delta > 0)
        {
            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookOverpayment,
                request.RawPayloadJson,
                externalTxId,
                delta,
                cancellationToken);

            await autoRefundService.CreateAndAttemptAsync(
                payment.Id,
                delta,
                reason: "overpayment-auto",
                enforceThreshold: true,
                cancellationToken);
        }

        return Result.Success(new MomoIpnResult(true));
    }

    private async Task RecordEventAsync(
        PaymentEntity payment,
        string eventType,
        string? payload,
        string? externalTransactionId,
        decimal? transferAmount,
        CancellationToken cancellationToken)
    {
        var ev = new PaymentEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            EventType = eventType,
            Payload = payload,
            Source = EventSource.WebhookMomo,
            ExternalTransactionId = externalTransactionId,
            TransferAmount = transferAmount,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await paymentEventRepository.AddAsync(ev, cancellationToken);
    }
}
