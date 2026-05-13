using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Authorization;
using UrbanX.Order.API.Abstractions;
using UrbanX.Order.Application.Usecases.V1.Command;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceOrder;
using UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;
using UrbanX.Order.Application.Usecases.V1.Query;

namespace UrbanX.Order.API.Apis;

public class OrderApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/orders";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("Order")
            .MapGroup(BaseUrl).HasApiVersion(1);

        group.MapPost("/", PlaceOrderV1);
        group.MapPost("/sales", PlaceSalesOrderV1)
            .WithSummary("Place a flash-sale order");
        group.MapGet("/my", ListMyOrdersV1);
        group.MapGet("/{id:guid}", GetOrderByIdV1);
        group.MapPut("/{id:guid}/confirm", ConfirmOrderV1);
        group.MapPut("/{id:guid}/cancel", CancelOrderV1);
    }

    public static async Task<IResult> PlaceOrderV1(
        [FromServices] ISender sender,
        [FromServices] IUserContext userContext,
        [FromBody] PlaceOrderCommand body,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        if (userId is null || userId == Guid.Empty)
            return Results.Problem(
                detail: "Authenticated user was not found in request context.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: "AUTH_REQUIRED");

        var result = await sender.Send(body with { UserId = userId.Value }, cancellationToken);
        if (result.IsFailure) return HandleFailure(result);
        return Results.Created($"/api/v1/orders/{result.Value}", result.Value);
    }

    private static async Task<IResult> PlaceSalesOrderV1(
        [FromServices] ISender sender,
        [FromServices] IUserContext userContext,
        [FromBody] PlaceSalesOrderCommand body,
        CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userId is null || userId == Guid.Empty)
            return Results.Problem(
                detail: "Authenticated user was not found in request context.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: "AUTH_REQUIRED");

        var result = await sender.Send(body with { UserId = userId.Value }, ct);
        if (result.IsFailure) return HandleFailure(result);
        return Results.Created($"/api/v1/orders/{result.Value}", result.Value);
    }

    public static async Task<IResult> ListMyOrdersV1(
        [FromServices] ISender sender,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new ListMyOrdersQuery(page, pageSize), cancellationToken);
        return ToOrderResult(result);
    }

    public static async Task<IResult> GetOrderByIdV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetOrderByIdQuery(id), cancellationToken);
        return ToOrderResult(result);
    }

    public static async Task<IResult> ConfirmOrderV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ConfirmOrderCommand(id), cancellationToken);
        if (result.IsFailure) return ToOrderResult(result);
        return Results.NoContent();
    }

    public static async Task<IResult> CancelOrderV1(
        [FromServices] ISender sender,
        [FromRoute] Guid id,
        [FromBody] CancelOrderBody body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelOrderCommand(id, body.Reason), cancellationToken);
        if (result.IsFailure) return ToOrderResult(result);
        return Results.NoContent();
    }
}

public record CancelOrderBody(string Reason);
