using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Exceptions;
using UrbanX.Inventory.API.Abstractions;
using UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

namespace UrbanX.Inventory.API.Apis;

public sealed class InternalReservationApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/internal/v{version:apiVersion}/reservations";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("InternalReservations").MapGroup(BaseUrl).HasApiVersion(1);
        group.MapPost("/", ReserveV1);
    }

    private static async Task<IResult> ReserveV1(
        [FromServices] ISender sender,
        [FromBody] ReserveInventoryCommand body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(body, cancellationToken);
            if (result.IsFailure)
                return ToReserveInventoryResult(result);

            var payload = result.Value!;
            return Results.Created($"/internal/v1/reservations/{payload.ReservationId}", payload);
        }
        catch (ConcurrencyRetryExhaustedException)
        {
            return Results.Json(
                new { type = "CONCURRENCY_RETRY_EXHAUSTED", title = "Concurrency conflict" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
