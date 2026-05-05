using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

[AllowAnonymous]
public record ReserveInventoryCommand(
    string IdempotencyKey,
    IReadOnlyList<ReserveInventoryLineItem> Items
) : ICommand<ReserveInventoryResponse>, IConcurrencyRetriableCommand;

public record ReserveInventoryLineItem(Guid ProductId, int Quantity);

public record ReserveInventoryResponse(
    Guid ReservationId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ReservedItemResponse> Items);

public record ReservedItemResponse(Guid ProductId, int Quantity);

public sealed class ReserveInventoryCommandValidator : AbstractValidator<ReserveInventoryCommand>
{
    public ReserveInventoryCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(50)
            .Must(k => Guid.TryParse(k, out _))
            .WithMessage("IdempotencyKey must be a valid UUID.");
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(i => i.Count <= 50)
            .WithMessage("At most 50 items are allowed.");
        RuleForEach(x => x.Items).ChildRules(line =>
        {
            line.RuleFor(i => i.ProductId).NotEmpty();
            line.RuleFor(i => i.Quantity).InclusiveBetween(1, 1000);
        });
    }
}
