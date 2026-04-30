using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Payment.API.Abstractions;
using UrbanX.Payment.Application.Usecases.V1.Command.CompleteRefund;
using UrbanX.Payment.Application.Usecases.V1.Query.GetRefundById;
using UrbanX.Payment.Application.Usecases.V1.Query.ListRefundsByPayment;

namespace UrbanX.Payment.API.Apis;

public class RefundApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/payments/{paymentId:guid}/refunds";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("Refund")
            .MapGroup(BaseURL).HasApiVersion(1);

        group1.MapGet("/", ListRefundsByPaymentV1);
        group1.MapGet("/{id:guid}", GetRefundByIdV1);
        group1.MapPost("/{id:guid}/complete", CompleteRefundV1);
    }

    private static async Task<IResult> ListRefundsByPaymentV1(
        [FromServices] ISender sender,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListRefundsByPaymentQuery(paymentId), cancellationToken);
        return ToPaymentResult(result);
    }

    private static async Task<IResult> GetRefundByIdV1(
        [FromServices] ISender sender,
        Guid paymentId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRefundByIdQuery(id), cancellationToken);
        return ToPaymentResult(result);
    }

    private static async Task<IResult> CompleteRefundV1(
        [FromServices] ISender sender,
        Guid paymentId,
        Guid id,
        [FromBody] CompleteRefundRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CompleteRefundCommand(id, body.ProviderRefundId), cancellationToken);
        if (result.IsFailure)
            return ToPaymentResult(result);
        return Results.NoContent();
    }
}

public record CompleteRefundRequest(string? ProviderRefundId);
