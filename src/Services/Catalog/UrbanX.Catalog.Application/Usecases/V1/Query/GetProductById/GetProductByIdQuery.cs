using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Catalog.Application.Usecases.V1.Query.GetProductById;

[AllowAnonymous]
public record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDetailResponse>;

public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator() =>
        RuleFor(x => x.ProductId).NotEmpty();
}

public record ProductDetailResponse(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string Status,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    decimal BasePrice,
    string? ShortDescription,
    string? PrimaryImageUrl,
    List<string> Tags,
    List<VariantReadDto> Variants,
    string? MetaTitle,
    string? MetaDescription,
    int? WeightGrams,
    DateTimeOffset UpdatedAt);

public record VariantReadDto(
    Guid Id,
    string Sku,
    string? Name,
    decimal Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    bool IsActive,
    List<AttributeReadDto> Attributes,
    List<string> GalleryImageUrls);

public record AttributeReadDto(string Name, string Value);
