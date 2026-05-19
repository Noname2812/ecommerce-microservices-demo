using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Contract.Messaging.Payment;
using Shared.Messaging;

namespace UrbanX.Payment.Application.Messaging.CreatePaymentSession;

/// <summary>
/// Demo consumer for the saga's <c>PaymentSessionCreating</c> state: receives
/// <see cref="CreatePaymentSessionV1"/> and immediately publishes a stub
/// <see cref="PaymentSessionCreatedV1"/> with a demo payment URL. No persistence,
/// no provider call — placeholder until a real payment integration is wired in.
/// </summary>
public sealed class CreatePaymentSessionConsumer(
    IPublishEndpoint publishEndpoint,
    ILogger<CreatePaymentSessionConsumer> logger)
    : IntegrationEventConsumerBase<CreatePaymentSessionV1, CreatePaymentSessionConsumer>(logger)
{
    private const int DemoSessionExpiryMinutes = 15;

    protected override async Task HandleAsync(CreatePaymentSessionV1 @event, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var paymentUrl = $"https://demo.payment.local/checkout/{@event.OrderId:N}";
        var qrCodeUrl = $"https://demo.payment.local/qr/{@event.OrderId:N}.png";

        logger.LogInformation(
            "Demo payment session created for OrderId={OrderId}, SessionId={SessionId}, Url={PaymentUrl}",
            @event.OrderId, sessionId, paymentUrl);

        await publishEndpoint.Publish(new PaymentSessionCreatedV1
        {
            CorrelationId    = @event.OrderId.ToString("D"),
            CausationId      = @event.EventId.ToString(),
            OrderId          = @event.OrderId,
            PaymentSessionId = sessionId,
            PaymentUrl       = paymentUrl,
            QrCodeUrl        = qrCodeUrl,
            ExpiresAt        = DateTimeOffset.UtcNow.AddMinutes(DemoSessionExpiryMinutes),
        }, cancellationToken);
    }
}
