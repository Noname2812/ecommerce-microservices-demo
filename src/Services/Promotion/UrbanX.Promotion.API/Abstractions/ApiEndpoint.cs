using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Application.Usecases.V1.Errors;

namespace UrbanX.Promotion.API.Abstractions;

public abstract class ApiEndpoint
{
    protected static IResult HandleFailure(Result result) => result switch
    {
        { IsSuccess: true } => throw new InvalidOperationException(),
        IValidationResult validationResult => Results.BadRequest(CreateProblemDetails(
            "Validation Error", 400, result.Error, validationResult.Errors)),
        _ => Results.BadRequest(CreateProblemDetails("Bad Request", 400, result.Error))
    };

    protected static IResult ToPromotionResult(Result result)
    {
        if (result is IValidationResult)
            return HandleFailure(result);
        if (result.IsSuccess)
            throw new InvalidOperationException("Expected a failed result.");

        var status = result.Error.Code switch
        {
            var c when c.EndsWith("NotFound") => StatusCodes.Status404NotFound,
            "FORBIDDEN" => StatusCodes.Status403Forbidden,
            "AUTH_REQUIRED" => StatusCodes.Status401Unauthorized,
            "Promotion.CannotModify" or
            "Promotion.CodeAlreadyUsed" or
            "Promotion.UsageLimitReached" or
            "Promotion.CustomerUsageLimitReached" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
    }

    protected static IResult ToPromotionResult<T>(Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : ToPromotionResult((Result)result);

    /// <summary>
    /// Maps DELETE release outcomes: 404 missing claim; 409 when status is not <c>CLAIMED</c>/<c>RELEASED</c> semantics (lifecycle conflict, not coupon quota exhaustion).
    /// </summary>
    protected static IResult ToReleaseCouponClaimResult(Result result)
    {
        if (result.IsSuccess)
            return Results.Ok();

        if (result is IValidationResult)
            return HandleFailure(result);

        var statusCode = result.Error.Code switch
        {
            var c when c.EndsWith("NotFound") => StatusCodes.Status404NotFound,
            "CouponClaim.InvalidStatus" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(detail: result.Error.Message, statusCode: statusCode, type: result.Error.Code);
    }

    protected static IResult ToCouponClaimResult(Result<ClaimCouponResult> result)
    {
        if (result.IsSuccess)
        {
            var v = result.Value!;
            return Results.Created($"/internal/v1/coupon-claims/{v.ClaimId}", v);
        }

        if (result is IValidationResult)
            return HandleFailure(result);

        var status = CouponErrors.MapsToHttp422(result.Error.Code)
            ? StatusCodes.Status422UnprocessableEntity
            : CouponErrors.MapsToHttp409(result.Error.Code)
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

        return Results.Problem(detail: result.Error.Message, statusCode: status, type: result.Error.Code);
    }

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
