namespace Shared.Contract.Messaging.Payment;

/// <summary>Scheduled timeout message — published by MassTransit scheduler after payment window expires.</summary>
public record PaymentExpiryTimeoutV1
{
    public Guid OrderId { get; init; }
}
