using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.Payment;
using Shared.Kernel.Primitives;
using UrbanX.Payment.Application.Abstractions;
using UrbanX.Payment.Domain;
using UrbanX.Payment.Domain.Models;
using UrbanX.Payment.Domain.ValueObjects;

namespace UrbanX.Payment.Application.Usecases.V1.Command.ExpirePayment;

internal sealed class ExpirePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentEventRepository paymentEventRepository,
    IAutoRefundService autoRefundService,
    IEventPublisher eventPublisher,
    ILogger<ExpirePaymentCommandHandler> logger) : ICommandHandler<ExpirePaymentCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Result> Handle(ExpirePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Success();

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.PartiallyPaid))
            return Result.Success();

        if (payment.ExpiresAt is null || payment.ExpiresAt >= DateTimeOffset.UtcNow)
            return Result.Success();

        payment.Status = PaymentStatus.Expired;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        var payload = JsonSerializer.Serialize(
            new { paidAmount = payment.PaidAmount, remainingAmount = payment.Amount - payment.PaidAmount },
            JsonOptions);

        await paymentEventRepository.AddAsync(
            new PaymentEvent
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = PaymentEventTypes.PaymentExpired,
                Payload = payload,
                Source = EventSource.Internal,
                CreatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        var expiredEvent = new PaymentIntegrationEvents.PaymentExpiredV1(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.Amount,
            payment.PaidAmount,
            payment.Amount - payment.PaidAmount,
            DateTimeOffset.UtcNow);

        await eventPublisher.PublishAsync(expiredEvent, cancellationToken);

        logger.LogInformation(
            "Payment {PaymentId} expired (PaidAmount={PaidAmount}, Amount={Amount}).",
            payment.Id, payment.PaidAmount, payment.Amount);

        // Auto-refund any partial amount that was received before expiry.
        // Skip the threshold gate — partial bank transfers must be returned in full regardless of size.
        if (payment.PaidAmount > 0)
        {
            var refundResult = await autoRefundService.CreateAndAttemptAsync(
                payment.Id,
                payment.PaidAmount,
                reason: "expiry-partial-auto",
                enforceThreshold: false,
                cancellationToken);

            if (refundResult.IsFailure)
            {
                logger.LogWarning(
                    "Auto-refund failed for expired payment {PaymentId}: {Code} {Message}",
                    payment.Id, refundResult.Error.Code, refundResult.Error.Message);
            }
        }

        return Result.Success();
    }
}
