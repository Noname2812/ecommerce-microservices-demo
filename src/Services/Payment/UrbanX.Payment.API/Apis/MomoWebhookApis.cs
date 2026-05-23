using System.Globalization;
using System.Text.Json;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UrbanX.Payment.API.Abstractions;
using UrbanX.Payment.Application.Configuration;
using UrbanX.Payment.Application.Usecases.V1.Command.HandleMomoIpn;
using UrbanX.Payment.Infrastructure.Integrations.Momo;

namespace UrbanX.Payment.API.Apis;

public sealed class MomoWebhookApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/payments";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("PaymentMomo").MapGroup(BaseURL).HasApiVersion(1);
        v1.MapPost("/webhook/momo", HandleMomoIpn);
    }

    private static async Task<IResult> HandleMomoIpn(
        HttpRequest request,
        [FromServices] ISender sender,
        [FromServices] IOptionsSnapshot<MomoOptions> momoOptions,
        ILogger<MomoWebhookApis> logger,
        CancellationToken cancellationToken)
    {
        MomoIpnInboundDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<MomoIpnInboundDto>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "MoMo IPN body is not valid JSON.");
            return Results.NoContent();
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.OrderId) || string.IsNullOrWhiteSpace(dto.Signature))
        {
            logger.LogWarning("MoMo IPN missing required fields.");
            return Results.NoContent();
        }

        var opts = momoOptions.Value;

        var signatureFields = new Dictionary<string, string?>
        {
            ["accessKey"]    = opts.AccessKey,
            ["amount"]       = dto.Amount.ToString(CultureInfo.InvariantCulture),
            ["extraData"]    = dto.ExtraData ?? string.Empty,
            ["message"]      = dto.Message ?? string.Empty,
            ["orderId"]      = dto.OrderId,
            ["orderInfo"]    = dto.OrderInfo ?? string.Empty,
            ["orderType"]    = dto.OrderType ?? string.Empty,
            ["partnerCode"]  = dto.PartnerCode ?? string.Empty,
            ["payType"]      = dto.PayType ?? string.Empty,
            ["requestId"]    = dto.RequestId ?? string.Empty,
            ["responseTime"] = dto.ResponseTime.ToString(CultureInfo.InvariantCulture),
            ["resultCode"]   = dto.ResultCode.ToString(CultureInfo.InvariantCulture),
            ["transId"]      = dto.TransId.ToString(CultureInfo.InvariantCulture)
        };

        if (!MomoSignature.Verify(signatureFields, opts.SecretKey, dto.Signature))
        {
            logger.LogWarning("MoMo IPN signature mismatch for orderId {OrderId}.", dto.OrderId);
            return Results.NoContent();
        }

        var rawJson = JsonSerializer.Serialize(dto, JsonOptions);

        var cmd = new HandleMomoIpnCommand(
            PartnerCode: dto.PartnerCode ?? string.Empty,
            OrderId: dto.OrderId,
            RequestId: dto.RequestId ?? string.Empty,
            Amount: dto.Amount,
            TransId: dto.TransId,
            ResultCode: dto.ResultCode,
            Message: dto.Message ?? string.Empty,
            OrderType: dto.OrderType ?? string.Empty,
            PayType: dto.PayType ?? string.Empty,
            ResponseTime: dto.ResponseTime,
            ExtraData: dto.ExtraData ?? string.Empty,
            Signature: dto.Signature,
            RawPayloadJson: rawJson);

        var result = await sender.Send(cmd, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "MoMo IPN handler failed for orderId {OrderId}: {Code} {Message}",
                dto.OrderId, result.Error.Code, result.Error.Message);
            return Results.NoContent();
        }

        return Results.Json(new { resultCode = 0, message = "Confirm Success" });
    }
}

public sealed class MomoIpnInboundDto
{
    public string? PartnerCode { get; set; }
    public string? OrderId { get; set; }
    public string? RequestId { get; set; }
    public decimal Amount { get; set; }
    public long TransId { get; set; }
    public int ResultCode { get; set; }
    public string? Message { get; set; }
    public string? OrderInfo { get; set; }
    public string? OrderType { get; set; }
    public string? PayType { get; set; }
    public long ResponseTime { get; set; }
    public string? ExtraData { get; set; }
    public string? Signature { get; set; }
}
