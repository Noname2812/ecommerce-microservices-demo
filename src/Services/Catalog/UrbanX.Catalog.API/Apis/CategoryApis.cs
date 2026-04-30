using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using UrbanX.Catalog.API.Abstractions;
using UrbanX.Catalog.Application.Usecases.V1.Command;

namespace UrbanX.Catalog.API.Apis
{
    public class CategoryApis : ApiEndpoint, ICarterModule
    {
        private const string BaseURL = "/api/v{version:apiVersion}/catalog";

        public void AddRoutes(IEndpointRouteBuilder app)
        {
            var group1 = app.NewVersionedApi("Category")
              .MapGroup(BaseURL).HasApiVersion(1);

            group1.MapPost("/category", CreateCategoryV1);
            group1.MapPut("/category/{categoryId:guid}", UpdateCategoryV1);
            group1.MapDelete("/category/{categoryId:guid}", DeleteCategoryV1);
        }

        public static async Task<IResult> CreateCategoryV1(
            [FromServices] ISender sender,
            [FromBody] CreateCategoryCommand body,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(body, cancellationToken);
            if (result.IsFailure)
                return HandleFailure(result);
            return Results.Created($"/api/v1/catalog/category/{result.Value}", result.Value);
        }

        public static async Task<IResult> UpdateCategoryV1(
            Guid categoryId,
            [FromServices] ISender sender,
            [FromBody] UpdateCategoryCommand body,
            CancellationToken cancellationToken)
        {
            var command = body with { CategoryId = categoryId };
            var result = await sender.Send(command, cancellationToken);
            return result.IsFailure ? ToCatalogResult(result) : Results.NoContent();
        }

        public static async Task<IResult> DeleteCategoryV1(
            Guid categoryId,
            [FromServices] ISender sender,
            CancellationToken cancellationToken)
        {
            var command = new DeleteCategoryCommand(categoryId);
            var result = await sender.Send(command, cancellationToken);
            if (result.IsFailure)
                return HandleFailure(result);
            return Results.NoContent();
        }
    }
}
