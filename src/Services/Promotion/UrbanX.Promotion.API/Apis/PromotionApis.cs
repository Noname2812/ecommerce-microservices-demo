using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Promotion.API.Abstractions;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Application.Usecases.V1.Query;

namespace UrbanX.Promotion.API.Apis;

public class PromotionApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/promotions";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("Promotion").MapGroup(BaseURL).HasApiVersion(1);

        v1.MapPost("/", Create);
        v1.MapGet("/", GetList);
        v1.MapGet("/{id:guid}", GetById);
        v1.MapPut("/{id:guid}", Update);
        v1.MapPost("/{id:guid}/activate", Activate);
        v1.MapPost("/{id:guid}/pause", Pause);
        v1.MapPost("/{id:guid}/voucher-codes", AddVoucherCodes);
        v1.MapPost("/{id:guid}/flash-sale-items", AddFlashSaleItems);
    }

    private static async Task<IResult> Create(
        [FromServices] ISender sender,
        [FromBody] CreatePromotionCommand body,
        CancellationToken ct)
    {
        var result = await sender.Send(body, ct);
        return result.IsSuccess
            ? Results.Created($"{BaseURL}/{result.Value}", result.Value)
            : ToPromotionResult(result);
    }

    private static async Task<IResult> GetList(
        [FromServices] ISender sender,
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new ListPromotionsQuery(type, status, pageIndex, pageSize), ct);
        return ToPromotionResult(result);
    }

    private static async Task<IResult> GetById(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetPromotionByIdQuery(id), ct);
        return ToPromotionResult(result);
    }

    private static async Task<IResult> Update(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromBody] UpdatePromotionCommand body,
        CancellationToken ct)
    {
        var result = await sender.Send(body with { Id = id }, ct);
        return result.IsSuccess ? Results.NoContent() : ToPromotionResult(result);
    }

    private static async Task<IResult> Activate(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var result = await sender.Send(new ActivatePromotionCommand(id), ct);
        return result.IsSuccess ? Results.NoContent() : ToPromotionResult(result);
    }

    private static async Task<IResult> Pause(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var result = await sender.Send(new PausePromotionCommand(id), ct);
        return result.IsSuccess ? Results.NoContent() : ToPromotionResult(result);
    }

    private static async Task<IResult> AddVoucherCodes(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromBody] AddVoucherCodesCommand body,
        CancellationToken ct)
    {
        var result = await sender.Send(body with { PromotionId = id }, ct);
        return result.IsSuccess ? Results.NoContent() : ToPromotionResult(result);
    }

    private static async Task<IResult> AddFlashSaleItems(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromBody] AddFlashSaleItemsCommand body,
        CancellationToken ct)
    {
        var result = await sender.Send(body with { PromotionId = id }, ct);
        return result.IsSuccess ? Results.NoContent() : ToPromotionResult(result);
    }
}
