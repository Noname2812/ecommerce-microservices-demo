using System.Text.Json;
using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Catalog.Domain.Errors;
using UrbanX.Catalog.Domain;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductById;

public sealed class GetProductByIdQueryHandler(IProductReadRepository repo)
    : IQueryHandler<GetProductByIdQuery, ProductDetailResponse>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Result<ProductDetailResponse>> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var view = await repo.GetByIdAsync(request.ProductId, ct);
        if (view is null)
            return Result.Failure<ProductDetailResponse>(CatalogErrors.ProductNotFound(request.ProductId));

        var variants = string.IsNullOrEmpty(view.VariantsJson)
            ? []
            : JsonSerializer.Deserialize<List<VariantReadDto>>(view.VariantsJson, JsonOpts) ?? [];

        return Result.Success(new ProductDetailResponse(
            view.ProductId,
            view.Sku,
            view.Name,
            view.Slug,
            view.Status,
            view.CategoryId,
            view.CategoryName,
            view.BrandId,
            view.BrandName,
            view.BasePrice,
            view.ShortDescription,
            view.PrimaryImageUrl,
            view.Tags.ToList(),
            variants,
            view.MetaTitle,
            view.MetaDescription,
            view.WeightGrams,
            view.UpdatedAt));
    }
}
