using System.Globalization;
using System.Text.Json;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Payment.API.Abstractions;
using UrbanX.Payment.API.Filters;
using UrbanX.Payment.Application.Usecases.V1.Command.HandleSePayWebhook;
using UrbanX.Payment.Application.Integrations.SePay;
using UrbanX.Payment.Application.Usecases.V1.Query.ResolveSePayWebhookPayment;

namespace UrbanX.Payment.API.Apis;

public sealed class SePayWebhookApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/payments";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("PaymentSePay").MapGroup(BaseURL).HasApiVersion(1);
        v1.MapPost("/webhook/sepay", HandleSePayWebhook)
            .AddEndpointFilter<SePayWebhookAuthFilter>();
    }

    private static async Task<IResult> HandleSePayWebhook(
        HttpRequest request,
        [FromServices] ISender sender,
        ILogger<SePayWebhookApis> logger,
        CancellationToken cancellationToken)
    {
        SePayWebhookInboundDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<SePayWebhookInboundDto>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { success = false, message = "invalid json" });
        }

        if (dto is null)
            return Results.BadRequest(new { success = false, message = "empty body" });

        if (!string.Equals(dto.TransferType, SePayIntegrationConstants.TransferTypeInbound, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { success = true });

        var content = dto.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(content))
        {
            logger.LogWarning("SePay webhook missing content");
            return Results.Json(new { success = true, message = "no match" });
        }

        var resolve = await sender.Send(new ResolveSePayWebhookPaymentQuery(content), cancellationToken);
        if (resolve.IsFailure)
            return ToPaymentResult(resolve);

        if (!resolve.Value.HasValue)
        {
            logger.LogWarning("SePay webhook could not match a payment for content snippet");
            return Results.Json(new { success = true, message = "no match" });
        }

        var paymentId = resolve.Value.Value;
        var externalId = dto.Id.ToString(CultureInfo.InvariantCulture);
        var rawJson = JsonSerializer.Serialize(dto, JsonOptions);

        var cmd = new HandleSePayWebhookCommand(
            paymentId,
            externalId,
            dto.TransferAmount,
            dto.TransferType ?? SePayIntegrationConstants.TransferTypeInbound,
            content,
            rawJson);

        var result = await sender.Send(cmd, cancellationToken);
        if (result.IsFailure)
            return ToPaymentResult(result);

        var ok = result.Value!;
        return Results.Json(new { success = ok.Success, message = ok.Message });
    }
}

public sealed class SePayWebhookInboundDto
{
    public long Id { get; set; }
    public string? Content { get; set; }
    public decimal TransferAmount { get; set; }
    public string? TransferType { get; set; }
}
