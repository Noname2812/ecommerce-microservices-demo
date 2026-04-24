using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Catalog.API.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command;

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
    }
}
