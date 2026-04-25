using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Catalog.API.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command;
using UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductBasicInfo;
using UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductVariants;
using UrbanX.Catalog.Application.Usecases.V1.Query.GetVariantDeleteEligibility;

namespace UrbanX.Catalog.API.Apis
{
    public class ProductApis : ApiEndpoint, ICarterModule
    {
        private const string BaseURL = "/api/v{version:apiVersion}/catalog/products";

        public void AddRoutes(IEndpointRouteBuilder app)
        {
            var group1 = app.NewVersionedApi("Product")
              .MapGroup(BaseURL).HasApiVersion(1);

            group1.MapPost("/", CreateProductV1);
            group1.MapPatch("/{productId:guid}", UpdateProductBasicInfoV1).RequireAuthorization();
            group1.MapPut("/{productId:guid}/variants", UpdateProductVariantsV1).RequireAuthorization();
            group1.MapGet("/{productId:guid}/variants/{variantId:guid}/delete-eligibility", GetVariantDeleteEligibilityV1).RequireAuthorization();
        }

        public static async Task<IResult> CreateProductV1(
            [FromServices] ISender sender,
            [FromBody] CreateProductCommand body,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(body, cancellationToken);
            if (result.IsFailure)
                return HandleFailure(result);
            return Results.Created($"/api/v1/catalog/products/{result.Value}", result.Value);
        }

        public static async Task<IResult> UpdateProductBasicInfoV1(
            Guid productId,
            [FromServices] ISender sender,
            [FromBody] UpdateProductBasicInfoCommand body,
            CancellationToken cancellationToken)
        {
            var command = body with { ProductId = productId };
            var result = await sender.Send(command, cancellationToken);
            return result.IsFailure ? ToCatalogResult(result) : Results.NoContent();
        }

        public static async Task<IResult> UpdateProductVariantsV1(
            Guid productId,
            [FromServices] ISender sender,
            [FromBody] UpdateProductVariantsCommand body,
            CancellationToken cancellationToken)
        {
            var command = body with { ProductId = productId };
            var result = await sender.Send(command, cancellationToken);
            return result.IsFailure ? ToCatalogResult(result) : Results.NoContent();
        }

        public static async Task<IResult> GetVariantDeleteEligibilityV1(
            Guid productId,
            Guid variantId,
            [FromServices] ISender sender,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(
                new GetVariantDeleteEligibilityQuery(productId, variantId),
                cancellationToken);
            return ToCatalogResult(result);
        }
    }
}
