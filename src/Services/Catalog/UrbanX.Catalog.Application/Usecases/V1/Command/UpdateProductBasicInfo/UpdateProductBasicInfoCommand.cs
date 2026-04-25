using FluentValidation;
using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Command.UpdateProductBasicInfo
{
    public record UpdateProductBasicInfoCommand(
        Guid ProductId,
        string Name,
        string? Slug,
        string? Description,
        string? ShortDescription,
        Guid? CategoryId,
        Guid? BrandId,
        decimal BasePrice,
        string? Status,
        int? WeightGrams,
        ProductDimensionsInput? Dimensions,
        IReadOnlyList<string>? Tags,
        string? MetaTitle,
        string? MetaDescription
    ) : ICommand;

    public sealed class UpdateProductBasicInfoCommandValidator : AbstractValidator<UpdateProductBasicInfoCommand>
    {
        public UpdateProductBasicInfoCommandValidator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
            RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Slug)
                .MaximumLength(500)
                .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
                .WithMessage("Slug must be lowercase, alphanumeric, hyphen-separated")
                .When(x => !string.IsNullOrEmpty(x.Slug));
            RuleFor(x => x.ShortDescription).MaximumLength(500).When(x => x.ShortDescription is not null);
            RuleFor(x => x.MetaTitle).MaximumLength(255).When(x => x.MetaTitle is not null);
            RuleFor(x => x.MetaDescription).MaximumLength(500).When(x => x.MetaDescription is not null);
        }
    }
}
