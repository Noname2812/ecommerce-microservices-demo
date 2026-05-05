using MediatR;
using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Inventory.Application.Messaging;
using UrbanX.Inventory.Application.Usecases.V1.Command.Release;
using Shared.Kernel.Primitives;

namespace UrbanX.Services.Inventory.UnitTests.Messaging;

public class InventoryReleaseRequestedProcessorTests
{
    private readonly Mock<IMediator> _mediator = new();

    private InventoryReleaseRequestedProcessor CreateProcessor() =>
        new(_mediator.Object);

    private static InventoryReleaseRequestedV1 EventWith(Guid eventId, Guid reservationId) =>
        new()
        {
            EventId = eventId,
            ReservationId = reservationId,
            Reason = "test",
            CorrelationId = "corr-1"
        };

    [Fact]
    public async Task ProcessAsync_WhenValid_SendsReleaseCommandWithEventId()
    {
        var eventId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var reservationId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var @event = EventWith(eventId, reservationId);

        _mediator
            .Setup(m => m.Send(It.IsAny<ReleaseInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await CreateProcessor().ProcessAsync(@event, CancellationToken.None);

        _mediator.Verify(
            m => m.Send(
                It.Is<ReleaseInventoryCommand>(c =>
                    c.ReservationId == reservationId && c.EventId == eventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenReleaseFails_ThrowsInventoryReleaseCommandFailedException()
    {
        var eventId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var reservationId = Guid.Parse("60000000-0000-4000-8000-000000000001");
        var @event = EventWith(eventId, reservationId);

        _mediator
            .Setup(m => m.Send(It.IsAny<ReleaseInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("InventoryReservation.NotFound", "x")));

        var ex = await Assert.ThrowsAsync<InventoryReleaseCommandFailedException>(() =>
            CreateProcessor().ProcessAsync(@event, CancellationToken.None));

        Assert.Equal("InventoryReservation.NotFound", ex.ErrorCode);
        Assert.Contains("InventoryReservation.NotFound", ex.Message, StringComparison.Ordinal);
    }
}
