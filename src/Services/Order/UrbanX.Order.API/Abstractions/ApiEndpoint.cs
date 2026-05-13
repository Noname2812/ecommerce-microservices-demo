using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;
using UrbanX.Order.Application.Usecases.V1.Errors;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace UrbanX.Order.API.Abstractions;

public abstract class ApiEndpoint
{
    protected static HttpResult HandleFailure(Result result) => result switch
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
        { Error.Code: "INVENTORY_OUT_OF_STOCK" or "COUPON_CLAIM_FAILED" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: result.Error.Code),
        { Error.Code: "INVENTORY_UNAVAILABLE" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: result.Error.Code),
        { Error.Code: "PRODUCT_NOT_FOUND" or "PRODUCT_UNAVAILABLE" or "SHIPPING_NOT_AVAILABLE" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: result.Error.Code),
        { Error.Code: "PRICE_MISMATCH" } => ToPriceMismatchResult(result.Error),
        { Error.Code: "Order.SaleQuotaExceeded" or "Order.SaleUserLimitExceeded" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status409Conflict,
            type: result.Error.Code),
        { Error.Code: "Order.SaleWindowExpired" or "Order.SaleCampaignInvalid" or "Order.SalePricingUnavailable" or "Order.PriceMismatch" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: result.Error.Code),
        IValidationResult validationResult => Results.BadRequest(CreateProblemDetails(
            "Validation Error", StatusCodes.Status400BadRequest, result.Error, validationResult.Errors)),
        _ => Results.BadRequest(CreateProblemDetails(
            "Bad Request", StatusCodes.Status400BadRequest, result.Error)),
    };

    protected static HttpResult ToOrderResult(Result result)
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
            "INVENTORY_OUT_OF_STOCK" or "COUPON_CLAIM_FAILED" => StatusCodes.Status409Conflict,
            "INVENTORY_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            "PRODUCT_NOT_FOUND" or "PRODUCT_UNAVAILABLE" or "SHIPPING_NOT_AVAILABLE" or "PRICE_MISMATCH"
                => StatusCodes.Status422UnprocessableEntity,
            "Order.SaleQuotaExceeded" or "Order.SaleUserLimitExceeded" => StatusCodes.Status409Conflict,
            "Order.SaleWindowExpired" or "Order.SaleCampaignInvalid" or "Order.SalePricingUnavailable" or "Order.PriceMismatch"
                => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };

        if (result.Error.Code == "PRICE_MISMATCH")
            return ToPriceMismatchResult(result.Error, status);

        return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
    }

    protected static HttpResult ToOrderResult<T>(Result<T> result) =>
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

    private static HttpResult ToPriceMismatchResult(Error error, int statusCode = StatusCodes.Status422UnprocessableEntity)
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
