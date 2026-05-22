using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Options;
using UrbanX.Promotion.Infrastructure.Jobs;

namespace UrbanX.Services.Promotion.UnitTests.Jobs;

public sealed class ReleaseExpiredCouponClaimsJobTests
{
    private readonly Mock<ICouponClaimRepository> _claimRepository = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<ILogger<ReleaseExpiredCouponClaimsJob>> _logger = new();
    private readonly IOptions<ReleaseExpiredCouponClaimsJobOptions> _options =
        Options.Create(new ReleaseExpiredCouponClaimsJobOptions());

    private ReleaseExpiredCouponClaimsJob CreateJob() =>
        new(_claimRepository.Object, _sender.Object, _options, _logger.Object);

    [Fact]
    public async Task ExecuteAsync_WhenExpiredClaimsExist_ReleasesExpiredClaims()
    {
        var expiredClaimOne = BuildClaim(
            Guid.Parse("10000000-0000-4000-8000-000000000001"),
            Guid.Parse("20000000-0000-4000-8000-000000000001"),
            DateTimeOffset.UtcNow.AddMinutes(-5));
        var expiredClaimTwo = BuildClaim(
            Guid.Parse("10000000-0000-4000-8000-000000000002"),
            Guid.Parse("20000000-0000-4000-8000-000000000002"),
            DateTimeOffset.UtcNow.AddMinutes(-1));

        _claimRepository
            .Setup(r => r.GetExpiredClaimedBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([expiredClaimOne, expiredClaimTwo]);

        _sender
            .Setup(s => s.Send(It.IsAny<ReleaseCouponClaimCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await CreateJob().ExecuteAsync(CancellationToken.None);

        _sender.Verify(
            s => s.Send(It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == expiredClaimOne.Id), It.IsAny<CancellationToken>()),
            Times.Once);
        _sender.Verify(
            s => s.Send(It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == expiredClaimTwo.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoExpiredClaims_DoesNotReleaseAnyClaim()
    {
        _claimRepository
            .Setup(r => r.GetExpiredClaimedBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateJob().ExecuteAsync(CancellationToken.None);

        _sender.Verify(
            s => s.Send(It.IsAny<ReleaseCouponClaimCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneReleaseFails_LogsWarningAndContinuesToNextClaim()
    {
        var failedClaim = BuildClaim(
            Guid.Parse("10000000-0000-4000-8000-000000000011"),
            Guid.Parse("20000000-0000-4000-8000-000000000011"),
            DateTimeOffset.UtcNow.AddMinutes(-5));
        var successfulClaim = BuildClaim(
            Guid.Parse("10000000-0000-4000-8000-000000000012"),
            Guid.Parse("20000000-0000-4000-8000-000000000012"),
            DateTimeOffset.UtcNow.AddMinutes(-1));

        _claimRepository
            .Setup(r => r.GetExpiredClaimedBatchAsync(200, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([failedClaim, successfulClaim]);

        _sender
            .Setup(s => s.Send(
                It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == failedClaim.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("CouponClaim.ReleaseFailed", "failed")));

        _sender
            .Setup(s => s.Send(
                It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == successfulClaim.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await CreateJob().ExecuteAsync(CancellationToken.None);

        _sender.Verify(
            s => s.Send(It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == failedClaim.Id), It.IsAny<CancellationToken>()),
            Times.Once);
        _sender.Verify(
            s => s.Send(It.Is<ReleaseCouponClaimCommand>(c => c.ClaimId == successfulClaim.Id), It.IsAny<CancellationToken>()),
            Times.Once);
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static CouponClaim BuildClaim(Guid claimId, Guid userId, DateTimeOffset expiresAt) =>
        new()
        {
            Id = claimId,
            CouponCode = "SAVE10",
            UserId = userId,
            OrderIdempotencyKey = $"order-{claimId:N}",
            DiscountAmount = 10_000,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            RestoreQuotaSlotOnRelease = true
        };
}
