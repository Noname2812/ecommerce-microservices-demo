using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Promotion.API.Abstractions;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Application.Usecases.V1.Query;

namespace UrbanX.Promotion.API.Apis;

public class PromotionValidateApis : ApiEndpoint, ICarterModule
{
    private const string BaseURL = "/api/v{version:apiVersion}/promotions";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("PromotionValidate").MapGroup(BaseURL).HasApiVersion(1);

        v1.MapPost("/redeem", Redeem);
        v1.MapPost("/preview", Preview);
    }

    private static async Task<IResult> Redeem(
        [FromServices] ISender sender,
        [FromBody] RedeemPromotionCommand body,
        CancellationToken ct)
    {
        var result = await sender.Send(body, ct);
        return ToPromotionResult(result);
    }

    private static async Task<IResult> Preview(
        [FromServices] ISender sender,
        [FromBody] PreviewDiscountQuery body,
        CancellationToken ct)
    {
        var result = await sender.Send(body, ct);
        return ToPromotionResult(result);
    }
}
