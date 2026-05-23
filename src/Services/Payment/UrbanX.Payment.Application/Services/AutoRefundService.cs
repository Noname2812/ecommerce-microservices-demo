using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Errors;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Services;

public sealed class AutoRefundService(
    IPaymentRepository paymentRepository,
    IRefundRepository refundRepository,
    IPaymentEventRepository paymentEventRepository,
    IEnumerable<IPaymentRefundProvider> refundProviders,
    IEventPublisher eventPublisher,
    IOptionsSnapshot<PaymentBusinessOptions> businessOptions,
    ILogger<AutoRefundService> logger) : IAutoRefundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Result<Guid?>> CreateAndAttemptAsync(
        Guid paymentId,
        decimal amount,
        string reason,
        bool enforceThreshold,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            return Result.Success<Guid?>(null);

        var threshold = businessOptions.Value.OverpaymentRefundThresholdVnd;
        if (enforceThreshold && amount <= threshold)
        {
            logger.LogInformation(
                "AutoRefund skipped for payment {PaymentId}: amount {Amount} <= threshold {Threshold}.",
                paymentId, amount, threshold);
            return Result.Success<Guid?>(null);
        }

        var payment = await paymentRepository.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<Guid?>(PaymentErrors.PaymentNotFound);

        var refund = new Refund
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            Amount = amount,
            Reason = reason
        };
        await refundRepository.AddAsync(refund, cancellationToken);

        await AppendEventAsync(
            payment.Id,
            PaymentEventTypes.AutoRefundCreated,
            JsonSerializer.Serialize(new { refundId = refund.Id, amount, reason }, JsonOptions),
            cancellationToken);

        var providerMethod = MapProviderNameToMethod(payment.ProviderName);
        var refundProvider = refundProviders.FirstOrDefault(p =>
            string.Equals(p.Method, providerMethod, StringComparison.OrdinalIgnoreCase));

        if (refundProvider is null)
        {
            // SEPay (or any provider without an automated refund API) stays Pending — admin processes manually.
            logger.LogInformation(
                "AutoRefund {RefundId} for payment {PaymentId} stays Pending — no auto provider for {Method}.",
                refund.Id, payment.Id, providerMethod);
            return Result.Success<Guid?>(refund.Id);
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
        {
            logger.LogWarning(
                "AutoRefund {RefundId} cannot reach provider — ProviderTransactionId missing on payment {PaymentId}.",
                refund.Id, payment.Id);
            return Result.Success<Guid?>(refund.Id);
        }

        var providerResult = await refundProvider.RefundAsync(
            refund.Id,
            payment.Id,
            payment.ProviderTransactionId!,
            amount,
            reason,
            cancellationToken);

        if (providerResult.IsFailure)
        {
            refund.MarkFailed();
            await AppendEventAsync(
                payment.Id,
                PaymentEventTypes.AutoRefundFailed,
                JsonSerializer.Serialize(
                    new { refundId = refund.Id, providerResult.Error.Code, providerResult.Error.Message },
                    JsonOptions),
                cancellationToken);
            return Result.Success<Guid?>(refund.Id);
        }

        refund.MarkCompleted(providerResult.Value);

        await AppendEventAsync(
            payment.Id,
            PaymentEventTypes.AutoRefundCompleted,
            JsonSerializer.Serialize(
                new { refundId = refund.Id, providerRefundId = providerResult.Value },
                JsonOptions),
            cancellationToken);

        var integrationEvent = new PaymentIntegrationEvents.RefundProcessedV1(
            refund.Id,
            refund.PaymentId,
            refund.OrderId,
            refund.Amount,
            payment.Currency,
            refund.ProviderRefundId,
            refund.ProcessedAt!.Value);
        await eventPublisher.PublishAsync(integrationEvent, cancellationToken);

        return Result.Success<Guid?>(refund.Id);
    }

    private Task AppendEventAsync(
        Guid paymentId, string type, string payload, CancellationToken ct) =>
        paymentEventRepository.AddAsync(
            new PaymentEvent
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                EventType = type,
                Payload = payload,
                Source = EventSource.Internal,
                CreatedAt = DateTimeOffset.UtcNow
            },
            ct);

    private static string MapProviderNameToMethod(string? providerName) => providerName switch
    {
        "MoMo" => ProviderType.Momo,
        "SePay" => ProviderType.Sepay,
        _ => providerName ?? string.Empty
    };
}
