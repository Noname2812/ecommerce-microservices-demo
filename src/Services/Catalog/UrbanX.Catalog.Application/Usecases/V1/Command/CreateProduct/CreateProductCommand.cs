using FluentValidation;
using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Command
{
    public record CreateProductCommand(
        string Sku,
        string Name,
        string? Slug,
        string? Description,
        string? ShortDescription,
        Guid CategoryId,
        Guid? BrandId,
        decimal BasePrice,
        Guid SellerId,
        string SellerName,
        string? Status,
        int? WeightGrams,
        ProductDimensionsInput? Dimensions,
        IReadOnlyList<string>? Tags,
        string? MetaTitle,
        string? MetaDescription,
        IReadOnlyList<CreateProductImageItem> ProductImages,
        IReadOnlyList<CreateProductVariantItem> Variants
    ) : ICommand;

    public record ProductDimensionsInput(
        decimal? LengthCm,
        decimal? WidthCm,
        decimal? HeightCm
    );

    public record CreateProductImageItem(
        string Url,
        string? AltText,
        int DisplayOrder,
        bool IsPrimary
    );

    public record CreateProductVariantItem(
        string Sku,
        string? Name,
        decimal Price,
        decimal? CompareAtPrice,
        string? ImageUrl,
        string? Barcode,
        IReadOnlyList<AttributeNameValueItem> Attributes,
        IReadOnlyList<CreateProductImageItem> GalleryImages
    );

    public record AttributeNameValueItem(
        string Name,
        string Value
    );

    public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
    {
        public CreateProductCommandValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(500);
            RuleFor(x => x.Slug).MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Slug));
            RuleFor(x => x.CategoryId).NotEmpty();
            RuleFor(x => x.SellerId).NotEmpty();
            RuleFor(x => x.SellerName).NotEmpty().MaximumLength(255);
            RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
            RuleFor(x => x.MetaTitle).MaximumLength(255);
            RuleFor(x => x.ShortDescription).MaximumLength(500);
            RuleFor(x => x.Variants)
                .NotNull()
                .NotEmpty();
            RuleFor(x => x.ProductImages).NotNull();
            RuleForEach(x => x.ProductImages)
                .ChildRules(
                    p => p
                        .RuleFor(i => i.Url)
                        .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
                        .WithMessage("Invalid product image URL"));
            RuleFor(x => x).Must(AllSkusUniqueInRequest).WithMessage("Product and variant SKUs must be unique within the request");
            RuleForEach(x => x.Variants).SetValidator(new CreateProductVariantItemValidator());
        }

        private static bool AllSkusUniqueInRequest(CreateProductCommand c)
        {
            var all = c.Variants.Select(v => v.Sku).Prepend(c.Sku);
            return all.Count() == all.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
    }

    public sealed class CreateProductVariantItemValidator : AbstractValidator<CreateProductVariantItem>
    {
        public CreateProductVariantItemValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Name).MaximumLength(255);
            RuleFor(x => x.Price)
                .GreaterThan(0)
                .LessThanOrEqualTo(1_000_000_000);
            RuleFor(x => x.CompareAtPrice)
                .GreaterThan(0)
                .When(x => x.CompareAtPrice is not null);
            RuleFor(x => x.Barcode).MaximumLength(100);
            RuleFor(x => x.ImageUrl)
                .Must(u => u == null || Uri.TryCreate(u, UriKind.Absolute, out _))
                .WithMessage("Invalid variant image URL");
            RuleFor(x => x.Attributes).NotNull();
            RuleFor(x => x.GalleryImages).NotNull();
            RuleForEach(x => x.Attributes)
                .ChildRules(a =>
                {
                    a.RuleFor(n => n.Name).NotEmpty().MaximumLength(100);
                    a.RuleFor(n => n.Value).NotEmpty().MaximumLength(255);
                });
            RuleForEach(x => x.GalleryImages)
                .ChildRules(img => img
                    .RuleFor(i => i.Url)
                    .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
                    .WithMessage("Invalid gallery image URL"));
        }
    }
}
