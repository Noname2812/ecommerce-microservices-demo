using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Infrastructure.Messaging.CouponReleaseRequested;

namespace UrbanX.Services.Promotion.UnitTests.Messaging;

public sealed class CouponReleaseRequestedConsumerTests
{
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<ILogger<CouponReleaseRequestedConsumer>> _logger = new();

    private CouponReleaseRequestedConsumer CreateConsumer() =>
        new(_sender.Object, _logger.Object);

    [Fact]
    public async Task Consume_SendsReleaseCommandWithClaimIdAndEventId()
    {
        var eventId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var claimId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var message = new CouponReleaseRequestedV1
        {
            EventId = eventId,
            ClaimId = claimId,
            Reason = "test",
            CorrelationId = "corr-1"
        };

        _sender
            .Setup(s => s.Send(It.IsAny<ReleaseCouponClaimCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var context = Mock.Of<ConsumeContext<CouponReleaseRequestedV1>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await CreateConsumer().Consume(context);

        _sender.Verify(
            s => s.Send(
                It.Is<ReleaseCouponClaimCommand>(cmd => cmd.ClaimId == claimId && cmd.EventId == eventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
