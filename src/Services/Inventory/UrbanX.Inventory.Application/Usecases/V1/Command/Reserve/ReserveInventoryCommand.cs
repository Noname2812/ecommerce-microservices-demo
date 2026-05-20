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
            .Must(BeValidIdempotencyKey)
            .WithMessage("IdempotencyKey must be a UUID or '{uuid}:inv' (place-order inventory shard).");
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

    private static bool BeValidIdempotencyKey(string key)
    {
        if (Guid.TryParse(key, out _))
            return true;

        const string suffix = ":inv";
        if (key.Length <= suffix.Length || !key.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var prefix = key[..^suffix.Length];
        return Guid.TryParse(prefix, out _);
    }
}
