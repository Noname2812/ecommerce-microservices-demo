using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Catalog.API.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command;
using UrbanX.Catalog.Application.Usecases.V1.Query;
using UrbanX.Catalog.Application.Usecases.V1.Query.GetProductById;
using UrbanX.Catalog.Application.Usecases.V1.Query.GetProductList;
using UrbanX.Catalog.Application.Usecases.V1.Query.SearchProducts;

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
            group1.MapGet("/product/{productId:guid}", GetProductByIdV1);
            group1.MapGet("/products", GetProductListV1);
            group1.MapGet("/product/{productId:guid}/variants/{variantId:guid}/delete-eligibility", GetVariantDeleteEligibilityV1);
            group1.MapPost("/internal/validate-products", ValidateProductsInternalV1);
            group1.MapPost("/internal/variant-prices", GetVariantPricesInternalV1);
            group1.MapGet("/variants/batch", GetVariantsBatchInternalV1);
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

        public static async Task<IResult> GetProductByIdV1(
            Guid productId,
            [FromServices] ISender sender,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(new GetProductByIdQuery(productId), cancellationToken);
            return ToCatalogResult(result);
        }

        public static async Task<IResult> GetProductListV1(
            [FromServices] ISender sender,
            CancellationToken cancellationToken,
            [FromQuery] string? q = null,
            [FromQuery] Guid? sellerId = null,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] string? status = null,
            [FromQuery] decimal? priceMin = null,
            [FromQuery] decimal? priceMax = null,
            [FromQuery] string sort = "relevance",
            [FromQuery] string? cursor = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (!string.IsNullOrWhiteSpace(q))
            {
                var searchResult = await sender.Send(
                    new SearchProductsQuery(q, categoryId, priceMin, priceMax, sort, page, pageSize),
                    cancellationToken);
                return ToCatalogResult(searchResult);
            }

            var result = await sender.Send(
                new GetProductListQuery(sellerId, categoryId, status, cursor, pageSize),
                cancellationToken);
            return ToCatalogResult(result);
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

        public static async Task<IResult> GetVariantsBatchInternalV1(
            [FromServices] ISender sender,
            [FromQuery] Guid[] ids,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(new GetVariantsBatchInternalQuery(ids), cancellationToken);
            return ToCatalogResult(result);
        }
    }
}
