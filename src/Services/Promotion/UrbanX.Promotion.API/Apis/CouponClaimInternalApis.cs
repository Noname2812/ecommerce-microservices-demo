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

        v1.MapDelete("/{claimId:guid}", ReleaseCouponClaim);
    }

    /// <summary>
    /// Releases a coupon claim (internal). Idempotent once the row is RELEASED.
    /// </summary>
    /// <remarks>
    /// HTTP callers (e.g. Order) SHOULD retry safely on transient failure including 500: SQL may commit <c>RELEASED</c> before Redis cleanup runs;
    /// a repeat DELETE treats an already released claim as 200 OK (no-op). See also <see cref="UrbanX.Promotion.Application.Logging.PromotionLogEvents.CouponClaimRedisPostCommitFailed"/>.
    /// </remarks>
    private static async Task<IResult> ReleaseCouponClaim(
        ISender sender,
        [FromRoute] Guid claimId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReleaseCouponClaimCommand(claimId), cancellationToken);
        return ToReleaseCouponClaimResult(result);
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
