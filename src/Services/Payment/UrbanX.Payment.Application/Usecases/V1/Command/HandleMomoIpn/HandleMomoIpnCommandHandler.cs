using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Integrations.Momo;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleMomoIpn;

internal sealed class HandleMomoIpnCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentEventRepository paymentEventRepository,
    IEventPublisher eventPublisher,
    ILogger<HandleMomoIpnCommandHandler> logger)
    : ICommandHandler<HandleMomoIpnCommand, MomoIpnResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

        if (payment.Status == PaymentStatus.Expired)
        {
            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceivedAfterExpiry,
                request.RawPayloadJson,
                externalTxId,
                request.Amount,
                cancellationToken);
            return Result.Success(new MomoIpnResult(true, "expired"));
        }

        var isSuccess = request.ResultCode is
            MomoIntegrationConstants.ResultCodeSuccess or MomoIntegrationConstants.ResultCodeAuthorized;
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

        if (!isSuccess)
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

        // Success path — MoMo guarantees exact amount on resultCode 0/9000
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
