using Moq;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;
using UrbanX.Inventory.Domain;

namespace UrbanX.Services.Inventory.UnitTests.Usecases.V1.Command.ConfirmReservation;

public sealed class ConfirmReservationCommandHandlerTests
{
    private readonly Mock<IInventoryReservationRepository> _reservations = new();
    private readonly Mock<IInventoryItemRepository> _items = new();

    private ConfirmReservationCommandHandler CreateHandler() =>
        new(_reservations.Object, _items.Object);

    [Fact]
    public async Task Handle_WhenRowsConfirmed_DeductsStockForEach()
    {
        var orderId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var itemId = Guid.Parse("60000000-0000-4000-8000-000000000001");

        _reservations
            .Setup(r => r.TryMarkConfirmedByOrderIdAsync(orderId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReservationConfirmResult(itemId, 2, orderId)]);

        var result = await CreateHandler().Handle(new ConfirmReservationCommand(orderId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _items.Verify(
            i => i.ConfirmDeductAtomicAsync(itemId, 2, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
