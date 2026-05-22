using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Payment;

namespace UrbanX.Payment.Infrastructure.Messaging.CreatePaymentSession;

/// <summary>
/// Demo consumer for the saga's <c>PaymentSessionCreating</c> state: publishes a stub
/// <see cref="PaymentSessionCreatedV1"/> until a real payment provider is wired in.
/// </summary>
public sealed class CreatePaymentSessionConsumer(
    IPublishEndpoint publishEndpoint,
    ILogger<CreatePaymentSessionConsumer> logger) : IConsumer<CreatePaymentSessionV1>
{
    private const int DemoSessionExpiryMinutes = 15;

    public async Task Consume(ConsumeContext<CreatePaymentSessionV1> context)
    {
        var @event = context.Message;
        var sessionId = Guid.NewGuid().ToString("N");
        var paymentUrl = $"https://demo.payment.local/checkout/{@event.OrderId:N}";
        var qrCodeUrl = $"https://demo.payment.local/qr/{@event.OrderId:N}.png";

        logger.LogInformation(
            "Demo payment session created for OrderId={OrderId}, SessionId={SessionId}, Url={PaymentUrl}",
            @event.OrderId,
            sessionId,
            paymentUrl);

        await publishEndpoint.Publish(new PaymentSessionCreatedV1
        {
            CorrelationId = @event.OrderId.ToString("D"),
            CausationId = @event.EventId.ToString(),
            OrderId = @event.OrderId,
            PaymentSessionId = sessionId,
            PaymentUrl = paymentUrl,
            QrCodeUrl = qrCodeUrl,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(DemoSessionExpiryMinutes),
        }, context.CancellationToken);
    }
}
