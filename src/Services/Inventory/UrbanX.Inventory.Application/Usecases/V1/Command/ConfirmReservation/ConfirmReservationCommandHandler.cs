using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Kernel.Primitives;

namespace UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

internal sealed class ConfirmReservationCommandHandler(
    ILogger<ConfirmReservationCommandHandler> logger)
    : IMessagingCommandHandler<ConfirmReservationCommand>
{
    public Task<Result> Handle(ConfirmReservationCommand cmd, CancellationToken ct)
    {
        // Idempotency: MassTransit InboxState handles duplicate event delivery at the consumer level
        // (DuplicateDetectionWindow + inbox_state table). Handler can focus on business logic.
        // No explicit SaveChanges — MT EF Outbox commits the consumer transaction after Consume returns.
        return Task.FromResult(Result.Success());
    }
}
