using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Integrations.SePay;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;
using PaymentEntity = UrbanX.Payment.Domain.Models.Payment;

namespace UrbanX.Payment.Application.Usecases.V1.Command.HandleSePayWebhook;

internal sealed class HandleSePayWebhookCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentEventRepository paymentEventRepository,
    IAutoRefundService autoRefundService,
    IEventPublisher eventPublisher,
    ILogger<HandleSePayWebhookCommandHandler> logger) : ICommandHandler<HandleSePayWebhookCommand, SePayWebhookResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<Result<SePayWebhookResult>> Handle(HandleSePayWebhookCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.TransferType, SePayIntegrationConstants.TransferTypeInbound, StringComparison.OrdinalIgnoreCase))
            return Result.Success(new SePayWebhookResult(true));

        if (await paymentEventRepository.ExistsByExternalTransactionIdAsync(request.ExternalTransactionId, cancellationToken))
            return Result.Success(new SePayWebhookResult(true));

        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
        {
            logger.LogWarning("SePay webhook referenced unknown payment {PaymentId}", request.PaymentId);
            return Result.Success(new SePayWebhookResult(true));
        }

        if (string.IsNullOrEmpty(payment.OrderNumber) ||
            !Regex.IsMatch(
                request.Content.Trim(),
                SePayIntegrationConstants.OrderNumberRegexWordBoundary +
                Regex.Escape(payment.OrderNumber) +
                SePayIntegrationConstants.OrderNumberRegexWordBoundary,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            logger.LogWarning(
                "SePay webhook payment {PaymentId} no longer matches content after lock; skipping.",
                request.PaymentId);
            return Result.Success(new SePayWebhookResult(true));
        }

        if (payment.Status == PaymentStatus.Completed)
            return Result.Success(new SePayWebhookResult(true, "already completed"));

        if (payment.Status == PaymentStatus.Expired)
        {
            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceivedAfterExpiry,
                JsonSerializer.Serialize(
                    new
                    {
                        transferAmount = request.TransferAmount,
                        expiredAt = payment.ExpiresAt ?? payment.UpdatedAt,
                        paidBeforeExpiry = payment.PaidAmount
                    },
                    JsonOptions),
                request.ExternalTransactionId,
                request.TransferAmount,
                cancellationToken);

            // Funds arrived after the order saga already gave up — refund full transfer regardless of threshold.
            await autoRefundService.CreateAndAttemptAsync(
                payment.Id,
                request.TransferAmount,
                reason: "cancelled-but-paid:expired",
                enforceThreshold: false,
                cancellationToken);

            return Result.Success(new SePayWebhookResult(true));
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            logger.LogWarning(
                "SePay webhook arrived after payment {PaymentId} was cancelled — issuing auto-refund.",
                payment.Id);

            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookReceivedAfterCancellation,
                JsonSerializer.Serialize(
                    new
                    {
                        transferAmount = request.TransferAmount,
                        cancelledAt = payment.UpdatedAt
                    },
                    JsonOptions),
                request.ExternalTransactionId,
                request.TransferAmount,
                cancellationToken);

            await autoRefundService.CreateAndAttemptAsync(
                payment.Id,
                request.TransferAmount,
                reason: "cancelled-but-paid:cancelled",
                enforceThreshold: false,
                cancellationToken);

            return Result.Success(new SePayWebhookResult(true, "cancelled-but-paid"));
        }

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.PartiallyPaid))
            return Result.Success(new SePayWebhookResult(true));

        var newPaidAmount = payment.PaidAmount + request.TransferAmount;
        var delta = newPaidAmount - payment.Amount;

        if (delta < 0)
        {
            payment.MarkPartiallyPaid(request.TransferAmount);

            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookPartialReceived,
                JsonSerializer.Serialize(
                    new
                    {
                        transferAmount = request.TransferAmount,
                        paidAmount = payment.PaidAmount,
                        remainingAmount = payment.RemainingAmount
                    },
                    JsonOptions),
                request.ExternalTransactionId,
                request.TransferAmount,
                cancellationToken);
            return Result.Success(new SePayWebhookResult(true));
        }

        payment.MarkCompletedViaBankTransfer(newPaidAmount, request.RawPayloadJson, request.ExternalTransactionId);

        await RecordEventAsync(
            payment,
            PaymentEventTypes.WebhookReceived,
            request.RawPayloadJson,
            request.ExternalTransactionId,
            request.TransferAmount,
            cancellationToken);

        if (delta > 0)
        {
            await RecordEventAsync(
                payment,
                PaymentEventTypes.WebhookOverpayment,
                JsonSerializer.Serialize(
                    new
                    {
                        delta,
                        paidAmount = payment.PaidAmount,
                        expectedAmount = payment.Amount
                    },
                    JsonOptions),
                null,
                null,
                cancellationToken);

            // Threshold-guarded so trivial round-ups don't trigger refund overhead.
            await autoRefundService.CreateAndAttemptAsync(
                payment.Id,
                delta,
                reason: "overpayment-auto",
                enforceThreshold: true,
                cancellationToken);
        }

        var integrationEvent = new PaymentIntegrationEvents.PaymentCompletedV1(
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

        await eventPublisher.PublishAsync(integrationEvent, cancellationToken);

        return Result.Success(new SePayWebhookResult(true));
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
            Source = EventSource.WebhookSepay,
            ExternalTransactionId = externalTransactionId,
            TransferAmount = transferAmount,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await paymentEventRepository.AddAsync(ev, cancellationToken);
    }
}
