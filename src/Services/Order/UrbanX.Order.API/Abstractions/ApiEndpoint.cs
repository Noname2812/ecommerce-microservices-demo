using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;

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
        { Error.Code: "FORBIDDEN" or "ORDER_FORBIDDEN" } => Results.Problem(
            detail: result.Error.Message,
            statusCode: StatusCodes.Status403Forbidden,
            type: result.Error.Code),
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
            "FORBIDDEN" or "ORDER_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "ORDER_NOT_FOUND" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
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
}
