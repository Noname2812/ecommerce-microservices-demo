using MassTransit;
using MediatR;
using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Inventory.Application.Usecases.V1.Command.Release;
using UrbanX.Inventory.Infrastructure.Messaging.InventoryReleaseRequested;

namespace UrbanX.Services.Inventory.UnitTests.Messaging;

public sealed class InventoryReleaseRequestedConsumerTests
{
    private readonly Mock<ISender> _sender = new();

    [Fact]
    public async Task Consume_SendsReleaseInventoryCommandWithOrderId()
    {
        var orderId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var message = new InventoryReleaseRequestedV1
        {
            OrderId = orderId,
            Reason = "cancelled",
            CorrelationId = "corr-1"
        };

        _sender
            .Setup(s => s.Send(It.IsAny<ReleaseInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shared.Kernel.Primitives.Result.Success());

        var context = Mock.Of<ConsumeContext<InventoryReleaseRequestedV1>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await new InventoryReleaseRequestedConsumer(_sender.Object).Consume(context);

        _sender.Verify(
            s => s.Send(
                It.Is<ReleaseInventoryCommand>(cmd => cmd.OrderId == orderId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
