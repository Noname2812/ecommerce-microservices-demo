using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace UrbanX.Inventory.API.Abstractions;

public abstract class ApiEndpoint
{
    protected static HttpIResult HandleFailure(Result result) => result switch
    {
        { IsSuccess: true } => throw new InvalidOperationException(),
        IValidationResult validationResult => Results.BadRequest(CreateProblemDetails(
            "Validation Error", 400, result.Error, validationResult.Errors)),
        _ => Results.BadRequest(CreateProblemDetails("Bad Request", 400, result.Error))
    };

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
