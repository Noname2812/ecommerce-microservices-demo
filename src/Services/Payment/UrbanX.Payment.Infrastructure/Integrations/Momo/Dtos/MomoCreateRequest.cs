using System.Text.Json.Serialization;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo.Dtos;

public sealed record MomoCreateRequest(
    [property: JsonPropertyName("partnerCode")] string PartnerCode,
    [property: JsonPropertyName("partnerName")] string PartnerName,
    [property: JsonPropertyName("storeId")]     string StoreId,
    [property: JsonPropertyName("requestId")]   string RequestId,
    [property: JsonPropertyName("amount")]      long Amount,
    [property: JsonPropertyName("orderId")]     string OrderId,
    [property: JsonPropertyName("orderInfo")]   string OrderInfo,
    [property: JsonPropertyName("redirectUrl")] string RedirectUrl,
    [property: JsonPropertyName("ipnUrl")]      string IpnUrl,
    [property: JsonPropertyName("lang")]        string Lang,
    [property: JsonPropertyName("extraData")]   string ExtraData,
    [property: JsonPropertyName("requestType")] string RequestType,
    [property: JsonPropertyName("signature")]   string Signature
);

public sealed record MomoCreateResponse(
    [property: JsonPropertyName("partnerCode")]  string? PartnerCode,
    [property: JsonPropertyName("requestId")]    string? RequestId,
    [property: JsonPropertyName("orderId")]      string? OrderId,
    [property: JsonPropertyName("amount")]       long Amount,
    [property: JsonPropertyName("responseTime")] long ResponseTime,
    [property: JsonPropertyName("message")]      string? Message,
    [property: JsonPropertyName("resultCode")]   int ResultCode,
    [property: JsonPropertyName("payUrl")]       string? PayUrl,
    [property: JsonPropertyName("deeplink")]     string? Deeplink,
    [property: JsonPropertyName("qrCodeUrl")]    string? QrCodeUrl
);
