using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

// Atomic CAS UPDATE in the handler eliminates xmin conflicts on inventory_items, but the unique
// constraint on (OrderIdempotencyKey, InventoryItemId) can still raise PostgreSQL 23505 when two
// concurrent deliveries of the same message both pass the idempotency check. IConcurrencyRetriableCommand
// gives EfUnitOfWork bounded retry; on the second attempt the idempotency lookup finds the
// just-committed row and returns Success — collapsing the duplicate into a single outcome.
[AllowAnonymous]
public record ReserveInventoryCommand(
    Guid OrderId,
    double ExpiresInMinutes,
    IReadOnlyList<ReserveInventoryLineItem> Items,
    Guid? EventId = null
) : ICommand<ReserveInventoryResponse>;

public record ReserveInventoryLineItem(Guid VariantId, int Quantity);

public record ReserveInventoryResponse(
    Guid OrderId,
    IReadOnlyCollection<Guid> ReservationIds,
    DateTimeOffset ExpiresAt
);


public sealed class ReserveInventoryCommandValidator : AbstractValidator<ReserveInventoryCommand>
{
    public ReserveInventoryCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty();
        
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(i => i.Count <= 50)
            .WithMessage("At most 50 items are allowed.")
            .Must(items => items.Select(i => i.VariantId).Distinct().Count() == items.Count)
            .WithMessage("Duplicate variant IDs are not allowed.");
        
        RuleForEach(x => x.Items).ChildRules(line =>
        {
            line.RuleFor(i => i.VariantId).NotEmpty();
            line.RuleFor(i => i.Quantity).InclusiveBetween(1, 1000);
        });
    }
}
