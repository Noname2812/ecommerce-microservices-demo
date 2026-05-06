using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Usecases.V1.Errors;

namespace UrbanX.Order.API.Abstractions;

public abstract class ApiEndpoint
{
    protected static IResult HandleFailure(Result result) => result switch
    {
        { IsSuccess: true } => throw new InvalidOperationException(),
        { Error.Code: "AUTH_REQUIRED" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status401Unauthorized,
            type: result.Error.Code),
        { Error.Code: "ORDER_RATE_LIMITED" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status429TooManyRequests,
            type: result.Error.Code),
        { Error.Code: "FORBIDDEN" or "ORDER_FORBIDDEN" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status403Forbidden,
            type: result.Error.Code),
        { Error.Code: "PRODUCT_NOT_FOUND" or "PRODUCT_UNAVAILABLE" or "SHIPPING_NOT_AVAILABLE" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: result.Error.Code),
        { Error.Code: "PRICE_MISMATCH" } => ToPriceMismatchResult(result.Error),
        IValidationResult validationResult => Results.BadRequest(CreateProblemDetails(
            "Validation Error", StatusCodes.Status400BadRequest, result.Error, validationResult.Errors)),
        _ => Results.BadRequest(CreateProblemDetails(
            "Bad Request", StatusCodes.Status400BadRequest, result.Error)),
    };

    protected static IResult ToOrderResult(Result result)
    {
        if (result is IValidationResult)
            return HandleFailure(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("Expected a failed result.");

        var status = result.Error.Code switch
        {
            "AUTH_REQUIRED" => StatusCodes.Status401Unauthorized,
            "ORDER_RATE_LIMITED" => StatusCodes.Status429TooManyRequests,
            "FORBIDDEN" or "ORDER_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "ORDER_NOT_FOUND" => StatusCodes.Status404NotFound,
            "PRODUCT_NOT_FOUND" or "PRODUCT_UNAVAILABLE" or "SHIPPING_NOT_AVAILABLE" or "PRICE_MISMATCH"
                => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };

        if (result.Error.Code == "PRICE_MISMATCH")
            return ToPriceMismatchResult(result.Error, status);

        return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
    }

    protected static IResult ToOrderResult<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToOrderResult((Result)result);

    private static ProblemDetails CreateProblemDetails(
        string title, int status, Error error, Error[]? errors = null) => new()
    {
        Title = title,
        Type = error.Code,
        Detail = error.Message,
        Status = status,
        Extensions = { { nameof(errors), errors } }
    };

    private static IResult ToPriceMismatchResult(Error error, int statusCode = StatusCodes.Status422UnprocessableEntity)
    {
        var details = new ProblemDetails
        {
            Title = "Price Mismatch",
            Type = error.Code,
            Detail = error.Message,
            Status = statusCode
        };

        if (error is PriceMismatchError mismatch)
            details.Extensions["currentPrice"] = mismatch.CurrentPrice;

        return Results.Problem(details);
    }
}
