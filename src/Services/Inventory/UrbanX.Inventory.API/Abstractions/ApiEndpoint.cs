using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.API.Abstractions;

public abstract class ApiEndpoint
{
    protected static IResult HandleFailure(Result result) => result switch
    {
        { IsSuccess: true } => throw new InvalidOperationException(),
        IValidationResult validationResult => Results.BadRequest(CreateProblemDetails(
            "Validation Error", 400, result.Error, validationResult.Errors)),
        _ => Results.BadRequest(CreateProblemDetails("Bad Request", 400, result.Error))
    };

    protected static IResult ToInventoryResult(Result result)
    {
        if (result is IValidationResult)
            return HandleFailure(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("Expected a failed result.");

        var status = result.Error.Code switch
        {
            var c when c.EndsWith("NotFound") => StatusCodes.Status404NotFound,
            "FORBIDDEN" => StatusCodes.Status403Forbidden,
            "OPTIMISTIC_LOCK_CONFLICT" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
    }

    protected static IResult ToInventoryResult<T>(Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : ToInventoryResult((Result)result);

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
