using Microsoft.Extensions.Logging.Abstractions;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.ConfirmReservation;

public class ConfirmReservationCommandHandlerTests
{
    private static ConfirmReservationCommandHandler CreateHandler() =>
        new(NullLogger<ConfirmReservationCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ReturnsSuccess()
    {
        // Idempotency now lives at the MassTransit InboxState layer (consumer-level).
        // Handler is currently a stub awaiting business logic; this test just guards the contract.
        var handler = CreateHandler();
        var result = await handler.Handle(
            new ConfirmReservationCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
