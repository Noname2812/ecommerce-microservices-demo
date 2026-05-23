using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrbanX.Payment.Infrastructure.Integrations.Momo.Dtos;

namespace UrbanX.Payment.Infrastructure.Integrations.Momo;

internal sealed class MomoClient(HttpClient httpClient, ILogger<MomoClient> logger) : IMomoClient
{
    private const string CreatePath = "/v2/gateway/api/create";
    private const string RefundPath = "/v2/gateway/api/refund";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<MomoCreateResponse> CreateSessionAsync(MomoCreateRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(CreatePath, request, JsonOptions, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "MoMo /create returned HTTP {Status} for orderId {OrderId}: {Body}",
                (int)response.StatusCode, request.OrderId, raw);
        }

        var payload = JsonSerializer.Deserialize<MomoCreateResponse>(raw, JsonOptions)
            ?? throw new InvalidOperationException("MoMo /create returned empty response.");
        return payload;
    }

    public async Task<MomoRefundResponse> RefundAsync(MomoRefundRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(RefundPath, request, JsonOptions, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "MoMo /refund returned HTTP {Status} for orderId {OrderId}: {Body}",
                (int)response.StatusCode, request.OrderId, raw);
        }

        var payload = JsonSerializer.Deserialize<MomoRefundResponse>(raw, JsonOptions)
            ?? throw new InvalidOperationException("MoMo /refund returned empty response.");
        return payload;
    }
}
