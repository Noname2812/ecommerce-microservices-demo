using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Catalog.API.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command;
using UrbanX.Catalog.Application.Usecases.V1.Query;

namespace UrbanX.Catalog.API.Apis
{
    public class ProductApis : ApiEndpoint, ICarterModule
    {
        private const string BaseURL = "/api/v{version:apiVersion}/catalog";

        public void AddRoutes(IEndpointRouteBuilder app)
        {
            var group1 = app.NewVersionedApi("Product")
              .MapGroup(BaseURL).HasApiVersion(1);

            group1.MapPost("/product", CreateProductV1);
            group1.MapPatch("/product/{productId:guid}", UpdateProductBasicInfoV1);
            group1.MapPut("/product/{productId:guid}/variants", UpdateProductVariantsV1);
            group1.MapGet("/product/{productId:guid}/variants/{variantId:guid}/delete-eligibility", GetVariantDeleteEligibilityV1);
            group1.MapPost("/internal/validate-products", ValidateProductsInternalV1);
            group1.MapPost("/internal/variant-prices", GetVariantPricesInternalV1);
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

        public static async Task<IResult> ValidateProductsInternalV1(
            [FromServices] ISender sender,
            [FromBody] ValidateProductsInternalQuery query,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(query, cancellationToken);
            return ToCatalogResult(result);
        }

        public static async Task<IResult> GetVariantPricesInternalV1(
            [FromServices] ISender sender,
            [FromBody] GetVariantPricesInternalQuery query,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(query, cancellationToken);
            return ToCatalogResult(result);
        }
    }
}
