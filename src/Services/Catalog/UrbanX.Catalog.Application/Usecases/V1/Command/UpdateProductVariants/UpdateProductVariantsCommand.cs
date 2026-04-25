using FluentValidation;

namespace UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductVariants
{
    public record UpdateProductVariantsCommand(
        Guid ProductId,
        IReadOnlyList<VariantSnapshotItem> Variants
    ) : Shared.Application.ICommand;

    public record VariantSnapshotItem(
        Guid? Id,
        string Sku,
        string? Name,
        decimal Price,
        decimal? CompareAtPrice,
        string? ImageUrl,
        string? Barcode,
        bool IsActive,
        IReadOnlyList<AttributeValueInput>? AttributeValues,
        IReadOnlyList<GalleryImageInput>? GalleryImages
    );

    public record AttributeValueInput(Guid AttributeDefinitionId, string Value);
    public record GalleryImageInput(string Url, string? AltText, int DisplayOrder, bool IsPrimary);

    public sealed class UpdateProductVariantsCommandValidator : AbstractValidator<UpdateProductVariantsCommand>
    {
        public UpdateProductVariantsCommandValidator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.Variants).NotEmpty().WithMessage("Snapshot must contain at least one variant");
            RuleFor(x => x.Variants)
                .Must(v => v.Any(i => i.IsActive))
                .WithMessage("At least one variant must be active");
            RuleFor(x => x.Variants)
                .Must(AllSkusUnique)
                .WithMessage("All variant SKUs in the snapshot must be unique");
            RuleForEach(x => x.Variants).SetValidator(new VariantSnapshotItemValidator());
        }

        private static bool AllSkusUnique(IReadOnlyList<VariantSnapshotItem> variants)
        {
            var skus = variants.Select(v => v.Sku);
            return skus.Count() == skus.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
    }

    public sealed class VariantSnapshotItemValidator : AbstractValidator<VariantSnapshotItem>
    {
        public VariantSnapshotItemValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Name).MaximumLength(255).When(x => x.Name is not null);
            RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(1_000_000_000);
            RuleFor(x => x.CompareAtPrice).GreaterThan(0).When(x => x.CompareAtPrice is not null);
            RuleFor(x => x.Barcode).MaximumLength(100).When(x => x.Barcode is not null);
            RuleFor(x => x.ImageUrl)
                .Must(u => u == null || Uri.TryCreate(u, UriKind.Absolute, out _))
                .WithMessage("Invalid variant image URL");
        }
    }
}
