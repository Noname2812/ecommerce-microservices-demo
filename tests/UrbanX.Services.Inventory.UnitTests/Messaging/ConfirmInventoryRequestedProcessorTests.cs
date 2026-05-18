using MediatR;
using Moq;
using Shared.Contract.Messaging.PlaceOrderSaga;
using Shared.Kernel.Primitives;
using UrbanX.Inventory.Application.Messaging;
using UrbanX.Inventory.Application.Usecases.V1.Command.ConfirmReservation;

namespace UrbanX.Services.Inventory.UnitTests.Messaging;

public class ConfirmInventoryRequestedProcessorTests
{
    [Fact]
    public async Task ProcessAsync_SendsConfirmReservationCommandWithEventFields()
    {
        var reservationId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var orderId = Guid.Parse("60000000-0000-4000-8000-000000000001");
        var eventId = Guid.Parse("90000000-0000-4000-8000-000000000001");
        var idempotencyKey = "33333333-3333-4333-8333-333333333333:confirm-inv";

        var sender = new Mock<ISender>();
        sender
            .Setup(m => m.Send(It.IsAny<ConfirmReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var processor = new ConfirmInventoryRequestedProcessor(sender.Object);
        var evt = new ConfirmInventoryRequestedV1
        {
            EventId = eventId,
            OrderId = orderId,
            ReservationId = reservationId,
            IdempotencyKey = idempotencyKey
        };

        await processor.ProcessAsync(evt, CancellationToken.None);

        sender.Verify(
            m => m.Send(
                It.Is<ConfirmReservationCommand>(c =>
                    c.ReservationId == reservationId &&
                    c.IdempotencyKey == idempotencyKey &&
                    c.EventId == eventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenCommandFails_ThrowsConfirmInventoryCommandFailedException()
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(m => m.Send(It.IsAny<ConfirmReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("InventoryReservation.NotFound", "missing")));

        var processor = new ConfirmInventoryRequestedProcessor(sender.Object);

        var ex = await Assert.ThrowsAsync<ConfirmInventoryCommandFailedException>(() =>
            processor.ProcessAsync(
                new ConfirmInventoryRequestedV1
                {
                    OrderId = Guid.NewGuid(),
                    ReservationId = Guid.NewGuid(),
                    IdempotencyKey = "k"
                },
                CancellationToken.None));

        Assert.Equal("InventoryReservation.NotFound", ex.ErrorCode);
        Assert.True(ex.IsPermanent);
    }
}
