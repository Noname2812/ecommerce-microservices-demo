using System.Text.Json.Serialization;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo.Dtos;

public sealed record MomoRefundRequest(
    [property: JsonPropertyName("partnerCode")] string PartnerCode,
    [property: JsonPropertyName("orderId")]     string OrderId,
    [property: JsonPropertyName("requestId")]   string RequestId,
    [property: JsonPropertyName("amount")]      long Amount,
    [property: JsonPropertyName("transId")]     long TransId,
    [property: JsonPropertyName("lang")]        string Lang,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("signature")]   string Signature
);

public sealed record MomoRefundResponse(
    [property: JsonPropertyName("partnerCode")]  string? PartnerCode,
    [property: JsonPropertyName("orderId")]      string? OrderId,
    [property: JsonPropertyName("requestId")]    string? RequestId,
    [property: JsonPropertyName("amount")]       long Amount,
    [property: JsonPropertyName("transId")]      long TransId,
    [property: JsonPropertyName("resultCode")]   int ResultCode,
    [property: JsonPropertyName("message")]      string? Message,
    [property: JsonPropertyName("responseTime")] long ResponseTime
);
