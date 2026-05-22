using FluentValidation;
using Shared.Application;
using Shared.Application.Authorization;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.Reserve;

// Dispatched from ReserveInventoryRequestedConsumer. IMessagingCommand skips TransactionPipelineBehavior
// because MassTransit EF Outbox already wraps the consumer in a DbContext transaction; double-wrapping
// causes "already in transaction" errors and breaks rollback semantics on Result.Failure.
[AllowAnonymous]
public record ReserveInventoryCommand(
    Guid OrderId,
    double ExpiresInMinutes,
    IReadOnlyList<ReserveInventoryLineItem> Items
) : IMessagingCommand;

public record ReserveInventoryLineItem(Guid VariantId, int Quantity);

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
