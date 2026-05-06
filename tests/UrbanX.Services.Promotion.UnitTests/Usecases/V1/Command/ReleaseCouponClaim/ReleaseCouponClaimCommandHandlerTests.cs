using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contract.Messaging.PlaceOrder;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Application.Usecases.V1.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Services.Promotion.UnitTests.Usecases.V1.Command.ReleaseCouponClaim;

public sealed class ReleaseCouponClaimCommandHandlerTests
{
    private readonly Mock<ICouponClaimRepository> _claimRepository = new();
    private readonly Mock<ICouponRepository> _couponRepository = new();
    private readonly Mock<IProcessedEventRepository> _processedEvents = new();
    private readonly Mock<ICouponClaimRedisGateway> _redisGateway = new();
    private readonly Mock<IPostCommitTaskQueue> _postCommitTasks = new();
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> _enqueued = new();

    private readonly ReleaseCouponClaimCommandHandler _handler;

    public ReleaseCouponClaimCommandHandlerTests()
    {
        _processedEvents
            .Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _postCommitTasks
            .Setup(q => q.Enqueue(It.IsAny<Func<CancellationToken, Task>>()))
            .Callback<Func<CancellationToken, Task>>(f => _enqueued.Enqueue(f));

        _handler = new ReleaseCouponClaimCommandHandler(
            _claimRepository.Object,
            _couponRepository.Object,
            _processedEvents.Object,
            _redisGateway.Object,
            _postCommitTasks.Object,
            NullLogger<ReleaseCouponClaimCommandHandler>.Instance);
    }

    private async Task DrainQueuedPostCommitAsync(CancellationToken cancellationToken = default)
    {
        while (_enqueued.TryDequeue(out var fn))
            await fn(cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenCompensationEventAlreadyProcessed_ReturnsSuccessWithoutSideEffects()
    {
        var claimId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        _processedEvents
            .Setup(r => r.ExistsAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId, eventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _claimRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _processedEvents.Verify(r => r.StageInsert(It.IsAny<ProcessedEvent>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenClaimNotFound_ReturnsNotFound()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(CouponClaimErrors.NotFound(claimId).Code, result.Error.Code);
        _postCommitTasks.Verify(q => q.Enqueue(It.IsAny<Func<CancellationToken, Task>>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyReleased_WithEventId_StagesProcessedEventOnly()
    {
        var claimId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "SAVE10",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Released,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ReleasedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = false
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId, eventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _processedEvents.Verify(
            r => r.StageInsert(It.Is<ProcessedEvent>(e =>
                e.EventId == eventId && e.EventType == nameof(ICouponReleaseRequested))),
            Times.Once);
        _postCommitTasks.Verify(q => q.Enqueue(It.IsAny<Func<CancellationToken, Task>>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyReleased_ReturnsSuccessWithoutPostCommitWork()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "SAVE10",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Released,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ReleasedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = false
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _claimRepository.Verify(
            r => r.TryMarkReleasedIfClaimedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _couponRepository.Verify(
            r => r.TryDecrementUsedQuotaIfPositiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _postCommitTasks.Verify(q => q.Enqueue(It.IsAny<Func<CancellationToken, Task>>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenClaimedAndQuotaCoupon_EnqueuesRedisWithRestore_DecrementsUsed()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "SAVE10",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = true
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _claimRepository
            .Setup(r =>
                r.TryMarkReleasedIfClaimedAsync(
                    claimId,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _couponRepository
            .Setup(r => r.TryDecrementUsedQuotaIfPositiveAsync("SAVE10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _redisGateway
            .Setup(g => g.ReleaseClaimRedisStateAsync("SAVE10", userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);
        await DrainQueuedPostCommitAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _couponRepository.Verify(
            r => r.TryDecrementUsedQuotaIfPositiveAsync("SAVE10", It.IsAny<CancellationToken>()),
            Times.Once);
        _postCommitTasks.Verify(q => q.Enqueue(It.IsAny<Func<CancellationToken, Task>>()), Times.Once);

        _redisGateway.Verify(
            g => g.ReleaseClaimRedisStateAsync("SAVE10", userId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenClaimedAndReleaseSucceeds_WithEventId_StagesProcessedEvent()
    {
        var claimId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "SAVE10",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = true
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _claimRepository
            .Setup(r =>
                r.TryMarkReleasedIfClaimedAsync(
                    claimId,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _couponRepository
            .Setup(r => r.TryDecrementUsedQuotaIfPositiveAsync("SAVE10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _redisGateway
            .Setup(g => g.ReleaseClaimRedisStateAsync("SAVE10", userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId, eventId), CancellationToken.None);
        await DrainQueuedPostCommitAsync();

        Assert.True(result.IsSuccess);
        _processedEvents.Verify(
            r => r.StageInsert(It.Is<ProcessedEvent>(e =>
                e.EventId == eventId && e.EventType == nameof(ICouponReleaseRequested))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUnlimitedCoupon_EnqueuesRedisWithoutQuotaRestore()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "FREE",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 0,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = false
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _claimRepository
            .Setup(r =>
                r.TryMarkReleasedIfClaimedAsync(
                    claimId,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _couponRepository
            .Setup(r => r.TryDecrementUsedQuotaIfPositiveAsync("FREE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _redisGateway
            .Setup(g => g.ReleaseClaimRedisStateAsync("FREE", userId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);
        await DrainQueuedPostCommitAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _redisGateway.Verify(
            g => g.ReleaseClaimRedisStateAsync("FREE", userId, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCouponRowMissing_StillRestoresRedisFromSnapshot()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "ORPHAN",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = true
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _claimRepository
            .Setup(r =>
                r.TryMarkReleasedIfClaimedAsync(
                    claimId,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _couponRepository
            .Setup(r => r.TryDecrementUsedQuotaIfPositiveAsync("ORPHAN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _redisGateway
            .Setup(g => g.ReleaseClaimRedisStateAsync("ORPHAN", userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);
        await DrainQueuedPostCommitAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _redisGateway.Verify(
            g => g.ReleaseClaimRedisStateAsync("ORPHAN", userId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenConcurrentWinnerAlreadyReleased_IsIdempotentWithoutRedis()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claimed = new CouponClaim
        {
            Id = claimId,
            CouponCode = "RACE",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = true
        };

        var released = new CouponClaim
        {
            Id = claimId,
            CouponCode = "RACE",
            UserId = userId,
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Released,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ReleasedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = true
        };

        _claimRepository
            .SetupSequence(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed)
            .ReturnsAsync(released);

        _claimRepository
            .Setup(r =>
                r.TryMarkReleasedIfClaimedAsync(
                    claimId,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);
        await DrainQueuedPostCommitAsync();

        // Assert
        Assert.True(result.IsSuccess);
        _couponRepository.Verify(
            r => r.TryDecrementUsedQuotaIfPositiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _postCommitTasks.Verify(q => q.Enqueue(It.IsAny<Func<CancellationToken, Task>>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStatusIsExpired_ReturnsInvalidStatus()
    {
        // Arrange
        var claimId = Guid.NewGuid();
        var claim = new CouponClaim
        {
            Id = claimId,
            CouponCode = "X",
            UserId = Guid.NewGuid(),
            OrderIdempotencyKey = "k",
            DiscountAmount = 1,
            Status = CouponClaimStatus.Expired,
            ExpiresAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = false
        };

        _claimRepository
            .Setup(r => r.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        // Act
        var result = await _handler.Handle(new ReleaseCouponClaimCommand(claimId), CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            CouponClaimErrors.InvalidStatusForRelease(CouponClaimStatus.Expired).Code,
            result.Error.Code);
        _claimRepository.Verify(
            r => r.TryMarkReleasedIfClaimedAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
