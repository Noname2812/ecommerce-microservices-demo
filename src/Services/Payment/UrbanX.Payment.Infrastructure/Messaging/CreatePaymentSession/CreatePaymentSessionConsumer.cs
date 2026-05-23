using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Payment;
using UrbanX.Payment.Application.Usecases.V1.Command.CreatePaymentSession;

namespace UrbanX.Payment.Infrastructure.Messaging.CreatePaymentSession;

/// <summary>
/// Translates the saga's <see cref="CreatePaymentSessionV1"/> into a Payment row + SePay VietQR URL,
/// then publishes <see cref="PaymentSessionCreatedV1"/> so the saga can advance.
/// </summary>
public sealed class CreatePaymentSessionConsumer(
    ISender sender,
    IPublishEndpoint publishEndpoint,
    ILogger<CreatePaymentSessionConsumer> logger) : IConsumer<CreatePaymentSessionV1>
{
    public async Task Consume(ConsumeContext<CreatePaymentSessionV1> context)
    {
        var evt = context.Message;
        var orderNumber = !string.IsNullOrWhiteSpace(evt.OrderNumber)
            ? evt.OrderNumber!
            : evt.OrderId.ToString("N")[..8].ToUpperInvariant();

        var cmd = new CreatePaymentSessionCommand(
            OrderId: evt.OrderId,
            OrderNumber: orderNumber,
            Amount: evt.Amount,
            Currency: evt.Currency,
            IdempotencyKey: evt.IdempotencyKey,
            CustomerId: evt.CustomerId,
            CustomerEmail: evt.CustomerEmail,
            PaymentMethod: evt.PaymentMethod);

        var result = await sender.Send(cmd, context.CancellationToken);
        if (result.IsFailure)
        {
            logger.LogError(
                "CreatePaymentSession failed for OrderId={OrderId}: {ErrorCode} {ErrorMessage}",
                evt.OrderId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"CreatePaymentSession failed for OrderId={evt.OrderId}: {result.Error.Code} {result.Error.Message}");
        }

        var paymentUrl = result.Value.PayUrl ?? result.Value.QrCodeUrl ?? string.Empty;

        await publishEndpoint.Publish(new PaymentSessionCreatedV1
        {
            CorrelationId = evt.OrderId.ToString("D"),
            CausationId = evt.EventId.ToString(),
            OrderId = evt.OrderId,
            PaymentSessionId = result.Value.PaymentId.ToString("N"),
            PaymentUrl = paymentUrl,
            QrCodeUrl = result.Value.QrCodeUrl,
            ExpiresAt = result.Value.ExpiresAt,
        }, context.CancellationToken);
    }
}
