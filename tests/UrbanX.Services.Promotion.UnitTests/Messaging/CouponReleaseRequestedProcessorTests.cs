using MediatR;
using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Messaging.CouponReleaseRequested;
using UrbanX.Promotion.Application.Usecases.V1.Command;

namespace UrbanX.Services.Promotion.UnitTests.Messaging;

public sealed class CouponReleaseRequestedProcessorTests
{
    private readonly Mock<IMediator> _mediator = new();

    private CouponReleaseRequestedProcessor CreateProcessor() => new(_mediator.Object);

    private static CouponReleaseRequestedV1 EventWith(Guid eventId, Guid claimId) =>
        new()
        {
            EventId = eventId,
            ClaimId = claimId,
            Reason = "test",
            CorrelationId = "corr-1"
        };

    [Fact]
    public async Task ProcessAsync_WhenValid_SendsReleaseCommandWithEventId()
    {
        var eventId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var claimId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var @event = EventWith(eventId, claimId);

        _mediator
            .Setup(m => m.Send(It.IsAny<ReleaseCouponClaimCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await CreateProcessor().ProcessAsync(@event, CancellationToken.None);

        _mediator.Verify(
            m => m.Send(
                It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == claimId && c.EventId == eventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenReleaseFails_ThrowsCouponReleaseCommandFailedException()
    {
        var eventId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var claimId = Guid.Parse("60000000-0000-4000-8000-000000000001");
        var @event = EventWith(eventId, claimId);

        _mediator
            .Setup(m => m.Send(It.IsAny<ReleaseCouponClaimCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("CouponClaim.NotFound", "x")));

        var ex = await Assert.ThrowsAsync<CouponReleaseCommandFailedException>(() =>
            CreateProcessor().ProcessAsync(@event, CancellationToken.None));

        Assert.Equal("CouponClaim.NotFound", ex.ErrorCode);
        Assert.Contains("CouponClaim.NotFound", ex.Message, StringComparison.Ordinal);
    }
}
