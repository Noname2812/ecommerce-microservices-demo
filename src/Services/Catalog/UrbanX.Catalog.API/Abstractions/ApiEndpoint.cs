using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace UrbanX.Catalog.API.Abstractions
{
    public abstract class ApiEndpoint
    {
        protected static HttpIResult HandleFailure(Result result) => result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(),
            { Error.Code: "AUTH_REQUIRED" } => Results.Problem(
                detail: result.Error.Message,
                statusCode: StatusCodes.Status401Unauthorized,
                type: result.Error.Code),
            { Error.Code: "FORBIDDEN" } => Results.Problem(
                detail: result.Error.Message,
                statusCode: StatusCodes.Status403Forbidden,
                type: result.Error.Code),
            IValidationResult validationResult => Results.BadRequest(CreateProblemDetails("Validation Error", StatusCodes.Status400BadRequest,
                result.Error, validationResult.Errors)),
            _ => Results.BadRequest(CreateProblemDetails("Bad Request", StatusCodes.Status400BadRequest,
                result.Error)),
        };

        /// <summary>Maps common catalog error <see cref="Error.Code"/> to HTTP status (404, 403, 409, 503).</summary>
        protected static HttpIResult ToCatalogResult(Result result)
        {
            if (result is IValidationResult)
                return HandleFailure(result);
            if (result.IsSuccess)
                throw new InvalidOperationException("Expected a failed result.");

            var status = result.Error.Code switch
            {
                "AUTH_REQUIRED" => StatusCodes.Status401Unauthorized,
                "FORBIDDEN" => StatusCodes.Status403Forbidden,
                "PRODUCT_NOT_FOUND" or "VARIANT_NOT_FOUND" or "CATEGORY_NOT_FOUND" or "BRAND_NOT_FOUND" => StatusCodes.Status404NotFound,
                "OPTIMISTIC_LOCK_CONFLICT" => StatusCodes.Status409Conflict,
                "INVENTORY_CHECK_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
        }

        /// <summary>On success: 200 with body. On failure: <see cref="ToCatalogResult(Result)"/>.</summary>
        protected static HttpIResult ToCatalogResult<T>(Result<T> result) =>
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
