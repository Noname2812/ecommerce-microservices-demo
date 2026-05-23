using System.Text.Json.Serialization;

namespace Shared.Contract.Dtos.Payment;

/// <summary>
/// Payment method selected by the user at checkout. Serialized as string ("Sepay" / "Momo")
/// to keep a stable wire format across RabbitMQ messages and HTTP bodies.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethod>))]
public enum PaymentMethod
{
    Sepay = 0,
    Momo = 1
}

public static class PaymentMethodExtensions
{
    /// <summary>
    /// Maps to the uppercase string stored in the DB (<c>payment_providers.type</c>) and used by
    /// <c>IPaymentSessionProvider.Method</c> — must match <c>ProviderType.Sepay</c> / <c>ProviderType.Momo</c>.
    /// </summary>
    public static string ToProviderTypeCode(this PaymentMethod method) => method switch
    {
        PaymentMethod.Sepay => "SEPAY",
        PaymentMethod.Momo => "MOMO",
        _ => method.ToString().ToUpperInvariant()
    };
}
