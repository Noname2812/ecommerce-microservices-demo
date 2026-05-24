using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.API.Abstractions;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace UrbanX.Promotion.API.Apis;

/// <summary>
/// Cart-time coupon hold endpoints — used by clients before checkout to reserve a coupon without
/// touching Postgres. The token returned here is verified by the Order saga at <c>PlaceOrder</c>.
/// </summary>
public sealed class CouponHoldApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/promotion/coupon-holds";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("CouponHold").MapGroup(BaseUrl).HasApiVersion(1);

        v1.MapPost("/", HoldCoupon);
        v1.MapDelete("/{token}", ReleaseCouponHold);
    }

    private static async Task<IResult> HoldCoupon(
        ISender sender,
        [FromBody] HoldCouponCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/promotion/coupon-holds/{result.Value!.HoldToken}", result.Value)
            : ToPromotionResult((Result<HoldCouponResult>)result);
    }

    private static async Task<IResult> ReleaseCouponHold(
        ISender sender,
        string token,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReleaseCouponHoldCommand(token), cancellationToken);
        return result.IsSuccess
            ? Results.NoContent()
            : ToPromotionResult(result);
    }
}
