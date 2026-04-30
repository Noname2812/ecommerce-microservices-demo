using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Payment.API.Abstractions;
using UrbanX.Payment.Application.Usecases.V1.Command.CancelPayment;
using UrbanX.Payment.Application.Usecases.V1.Command.CompletePayment;
using UrbanX.Payment.Application.Usecases.V1.Command.FailPayment;
using UrbanX.Payment.Application.Usecases.V1.Command.ProcessPayment;
using UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentById;
using UrbanX.Payment.Application.Usecases.V1.Query.GetPaymentByOrderId;
using UrbanX.Payment.Application.Usecases.V1.Query.ListPayments;

namespace UrbanX.Payment.API.Apis;

public class PaymentApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/payments";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("Payment")
            .MapGroup(BaseURL).HasApiVersion(1);

        group1.MapGet("/", ListPaymentsV1);
        group1.MapGet("/{id:guid}", GetPaymentByIdV1);
        group1.MapGet("/by-order/{orderId:guid}", GetPaymentByOrderIdV1);
        group1.MapPost("/{id:guid}/process", ProcessPaymentV1);
        group1.MapPost("/{id:guid}/complete", CompletePaymentV1);
        group1.MapPost("/{id:guid}/fail", FailPaymentV1);
        group1.MapPost("/{id:guid}/cancel", CancelPaymentV1);
    }

    private static async Task<IResult> ListPaymentsV1(
        [FromServices] ISender sender,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new ListPaymentsQuery(page, pageSize, status, customerId, from, to), cancellationToken);
        return ToPaymentResult(result);
    }

    private static async Task<IResult> GetPaymentByIdV1(
        [FromServices] ISender sender,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPaymentByIdQuery(id), cancellationToken);
        return ToPaymentResult(result);
    }

    private static async Task<IResult> GetPaymentByOrderIdV1(
        [FromServices] ISender sender,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPaymentByOrderIdQuery(orderId), cancellationToken);
        return ToPaymentResult(result);
    }

    private static async Task<IResult> ProcessPaymentV1(
        [FromServices] ISender sender,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ProcessPaymentCommand(id), cancellationToken);
        if (result.IsFailure)
            return ToPaymentResult(result);
        return Results.NoContent();
    }

    private static async Task<IResult> CompletePaymentV1(
        [FromServices] ISender sender,
        Guid id,
        [FromBody] CompletePaymentRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CompletePaymentCommand(id, body.ProviderTransactionId), cancellationToken);
        if (result.IsFailure)
            return ToPaymentResult(result);
        return Results.NoContent();
    }

    private static async Task<IResult> FailPaymentV1(
        [FromServices] ISender sender,
        Guid id,
        [FromBody] FailPaymentRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new FailPaymentCommand(id, body.FailureReason), cancellationToken);
        if (result.IsFailure)
            return ToPaymentResult(result);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelPaymentV1(
        [FromServices] ISender sender,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelPaymentCommand(id), cancellationToken);
        if (result.IsFailure)
            return ToPaymentResult(result);
        return Results.NoContent();
    }
}

public record CompletePaymentRequest(string? ProviderTransactionId);
public record FailPaymentRequest(string FailureReason);
