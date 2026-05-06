using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Promotion.API.Abstractions;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Promotion.API.Apis;

public sealed class CouponClaimInternalApis : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/internal/v{version:apiVersion}/coupon-claims";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("CouponClaimInternal").MapGroup(BaseUrl).HasApiVersion(1);

        v1.MapPost("/", ClaimCoupon);
    }

    private static async Task<IResult> ClaimCoupon(
        ISender sender,
        [FromBody] ClaimCouponCommand body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(body, cancellationToken);
        return ToCouponClaimResult(result);
    }
}
