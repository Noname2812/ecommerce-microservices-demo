using FluentValidation;
using Shared.Application;

namespace UrbanX.Catalog.Application.Usecases.V1.Command
{
    public record UpdateCategoryCommand(
        Guid CategoryId,    
        string Name,
        string? Slug,
        string? Description,
        Guid? ParentCategoryId,
        string? MetaTitle,
        string? MetaDescription
    ) : ICommand<Guid>;

    public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
    {
        public UpdateCategoryCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Slug).MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(1000);
            RuleFor(x => x.MetaTitle).MaximumLength(255);
            RuleFor(x => x.MetaDescription).MaximumLength(1000);
        }
    }
}