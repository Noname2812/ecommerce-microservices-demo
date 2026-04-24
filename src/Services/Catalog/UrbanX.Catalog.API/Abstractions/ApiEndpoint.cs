using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;

namespace UrbanX.Catalog.API.Abstractions
{
    public abstract class ApiEndpoint
    {
        protected static IResult HandleFailure(Result result) => result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(),
            IValidationResult validationResult => Results.BadRequest(CreateProblemDetails("Validation Error", StatusCodes.Status400BadRequest,
                result.Error, validationResult.Errors)),
            _ => Results.BadRequest(CreateProblemDetails("Bad Request", StatusCodes.Status400BadRequest,
                result.Error)),
        };

        /// <summary>Maps common catalog error <see cref="Error.Code"/> to HTTP status (404, 403, 409, 503).</summary>
        protected static IResult ToCatalogResult(Result result)
        {
            if (result is IValidationResult)
                return HandleFailure(result);
            if (result.IsSuccess)
                throw new InvalidOperationException("Expected a failed result.");

            var status = result.Error.Code switch
            {
                "PRODUCT_NOT_FOUND" or "VARIANT_NOT_FOUND" => StatusCodes.Status404NotFound,
                "FORBIDDEN" => StatusCodes.Status403Forbidden,
                "OPTIMISTIC_LOCK_CONFLICT" => StatusCodes.Status409Conflict,
                "INVENTORY_CHECK_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
        }

        /// <summary>On success: 200 with body. On failure: <see cref="ToCatalogResult(Result)"/>.</summary>
        protected static IResult ToCatalogResult<T>(Result<T> result) =>
            result.IsSuccess
                ? Results.Ok(result.Value)
                : ToCatalogResult((Result)result);

        private static ProblemDetails CreateProblemDetails(string title, int status, Error error, Error[]? errors = null) => new()
        {
            Title = title,
            Type = error.Code,
            Detail = error.Message,
            Status = status,
            Extensions = { { nameof(errors), errors } }
        };
    }
}
