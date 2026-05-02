using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

[RequirePermission(Permissions.Promotions.Write)]
public record AddVoucherCodesCommand(
    Guid PromotionId,
    IReadOnlyList<AddVoucherCodeItem> Codes
) : ICommand;

public record AddVoucherCodeItem(string Code, Guid? AssignedToCustomerId);

public sealed class AddVoucherCodesCommandValidator : AbstractValidator<AddVoucherCodesCommand>
{
    public AddVoucherCodesCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
        RuleFor(x => x.Codes).NotEmpty();
        RuleForEach(x => x.Codes).ChildRules(item =>
        {
            item.RuleFor(i => i.Code).NotEmpty().MaximumLength(100)
                .Matches("^[A-Za-z0-9_-]+$").WithMessage("Code must contain only letters, digits, hyphens or underscores");
        });
        RuleFor(x => x).Must(c => c.Codes.Select(i => i.Code.ToUpperInvariant()).Distinct().Count() == c.Codes.Count)
            .WithMessage("Codes must be unique within the request");
    }
}
