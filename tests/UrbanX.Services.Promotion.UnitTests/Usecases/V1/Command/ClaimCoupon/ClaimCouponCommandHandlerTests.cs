using Moq;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Application.Usecases.V1.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;
using UrbanX.Promotion.Application.Abstractions;

namespace UrbanX.Services.Promotion.UnitTests.Usecases.V1.Command.ClaimCoupon;

public sealed class ClaimCouponCommandHandlerTests
{
    private readonly Mock<ICouponRepository> _couponRepository = new();
    private readonly Mock<ICouponClaimRepository> _claimRepository = new();
    private readonly Mock<ICouponClaimRedisGateway> _redisGateway = new();

    private readonly ClaimCouponCommandHandler _handler;

    public ClaimCouponCommandHandlerTests()
    {
        _handler = new ClaimCouponCommandHandler(
            _couponRepository.Object,
            _claimRepository.Object,
            _redisGateway.Object);
    }

    [Fact]
    public async Task Handle_TC_CPN_001_ValidUnlimitedCoupon_ReturnsClaim()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var coupon = new Coupon
        {
            Id = "WELCOME10",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10,
            TotalQuota = null,
            UsedQuota = 0,
            MinOrderValue = 0,
            ValidFrom = now.AddDays(-1),
            ExpiresAt = now.AddDays(30),
            IsActive = true
        };

        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync("idem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);

        _couponRepository
            .Setup(r => r.GetByCodeAsync("WELCOME10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coupon);

        _redisGateway
            .Setup(g => g.TryAcquireUserHoldAsync("WELCOME10", userId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        CouponClaim? captured = null;
        _claimRepository
            .Setup(r => r.AddAsync(It.IsAny<CouponClaim>(), It.IsAny<CancellationToken>()))
            .Callback<CouponClaim, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        _couponRepository.Setup(r => r.UpdateAsync(It.IsAny<Coupon>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cmd = new ClaimCouponCommand("idem-1", "welcome10", userId, 100_000m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("WELCOME10", captured!.CouponCode);
        Assert.Equal(userId, captured.UserId);
        Assert.Equal("idem-1", captured.OrderIdempotencyKey);
        Assert.False(captured.RestoreQuotaSlotOnRelease);
        Assert.Equal(10_000m, result.Value!.DiscountAmount);

        _redisGateway.Verify(
            g => g.TryConsumeQuotaSlotAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _couponRepository.Verify(r => r.UpdateAsync(It.Is<Coupon>(c => c.UsedQuota == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TC_CPN_002_CouponMissing_ReturnsNotFound()
    {
        // Arrange
        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);

        _couponRepository
            .Setup(r => r.GetByCodeAsync("NONE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Coupon?)null);

        var cmd = new ClaimCouponCommand("k", "NONE", Guid.NewGuid(), 10m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(CouponErrors.NotFound.Code, result.Error.Code);
        _redisGateway.Verify(
            g => g.TryAcquireUserHoldAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TC_CPN_003_UserLockExists_ReturnsAlreadyUsed()
    {
        // Arrange
        var coupon = ActivePercentageCoupon("USED", minOrder: 0);
        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);
        _couponRepository
            .Setup(r => r.GetByCodeAsync("USED", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coupon);

        _redisGateway
            .Setup(g => g.TryAcquireUserHoldAsync("USED", It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new ClaimCouponCommand("k", "USED", Guid.NewGuid(), 50m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(CouponErrors.AlreadyUsed.Code, result.Error.Code);
        _claimRepository.Verify(
            r => r.AddAsync(It.IsAny<CouponClaim>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TC_CPN_004_QuotaExhausted_RollsBackAndReturnsExhausted()
    {
        // Arrange
        var coupon = ActivePercentageCoupon("LIMIT", minOrder: 0, totalQuota: 5, usedQuota: 5);
        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);
        _couponRepository
            .Setup(r => r.GetByCodeAsync("LIMIT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coupon);

        _redisGateway
            .Setup(g => g.TryAcquireUserHoldAsync("LIMIT", It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _redisGateway
            .Setup(g => g.TryConsumeQuotaSlotAsync("LIMIT", It.IsAny<Guid>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new ClaimCouponCommand("k", "LIMIT", Guid.NewGuid(), 100m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(CouponErrors.Exhausted.Code, result.Error.Code);
        _claimRepository.Verify(
            r => r.AddAsync(It.IsAny<CouponClaim>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _couponRepository.Verify(r => r.UpdateAsync(It.IsAny<Coupon>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TC_CPN_005_IdempotentSecondCall_ReturnsSameClaim()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var existing = new CouponClaim
        {
            Id = existingId,
            CouponCode = "WELCOME10",
            UserId = Guid.NewGuid(),
            OrderIdempotencyKey = "order-key-1",
            DiscountAmount = 500m,
            Status = CouponClaimStatus.Claimed,
            ExpiresAt = expires,
            CreatedAt = DateTimeOffset.UtcNow,
            RestoreQuotaSlotOnRelease = false
        };

        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync("order-key-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var cmd = new ClaimCouponCommand("order-key-1", "ANY", Guid.NewGuid(), 100m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(existingId, result.Value!.ClaimId);
        Assert.Equal(500m, result.Value.DiscountAmount);
        Assert.Equal(expires, result.Value.ExpiresAt);
        _couponRepository.Verify(
            r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Same rollback path as TC-CPN-004; spec's "100 users / quota 5" needs Redis integration tests.
    /// </summary>
    [Fact]
    public async Task Handle_TC_CPN_006_LastSlotExhausted_RollsBackCorrectly()
    {
        // Arrange
        var coupon = ActivePercentageCoupon("QUOTA5", minOrder: 0, totalQuota: 5, usedQuota: 0);
        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);
        _couponRepository
            .Setup(r => r.GetByCodeAsync("QUOTA5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coupon);

        _redisGateway
            .Setup(g => g.TryAcquireUserHoldAsync("QUOTA5", It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _redisGateway
            .Setup(g => g.TryConsumeQuotaSlotAsync("QUOTA5", It.IsAny<Guid>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new ClaimCouponCommand("new-key", "QUOTA5", Guid.NewGuid(), 50m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(CouponErrors.Exhausted.Code, result.Error.Code);
        _redisGateway.Verify(
            g => g.TryConsumeQuotaSlotAsync("QUOTA5", It.IsAny<Guid>(), 5, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenQuotaCoupon_SnapshotSetsRestoreQuotaTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var coupon = new Coupon
        {
            Id = "LIMITED10",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10,
            TotalQuota = 10,
            UsedQuota = 0,
            MinOrderValue = 0,
            ValidFrom = now.AddDays(-1),
            ExpiresAt = now.AddDays(30),
            IsActive = true
        };

        _claimRepository
            .Setup(r => r.GetByIdempotencyKeyAsync("kq", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CouponClaim?)null);

        _couponRepository
            .Setup(r => r.GetByCodeAsync("LIMITED10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coupon);

        _redisGateway
            .Setup(g => g.TryAcquireUserHoldAsync("LIMITED10", userId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _redisGateway
            .Setup(g => g.TryConsumeQuotaSlotAsync("LIMITED10", userId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        CouponClaim? captured = null;
        _claimRepository
            .Setup(r => r.AddAsync(It.IsAny<CouponClaim>(), It.IsAny<CancellationToken>()))
            .Callback<CouponClaim, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        _couponRepository.Setup(r => r.UpdateAsync(It.IsAny<Coupon>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cmd = new ClaimCouponCommand("kq", "LIMITED10", userId, 100_000m);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.RestoreQuotaSlotOnRelease);
    }

    private static Coupon ActivePercentageCoupon(
        string id,
        decimal minOrder,
        int? totalQuota = null,
        int usedQuota = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new Coupon
        {
            Id = id,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10,
            TotalQuota = totalQuota,
            UsedQuota = usedQuota,
            MinOrderValue = minOrder,
            ValidFrom = now.AddDays(-1),
            ExpiresAt = now.AddDays(30),
            IsActive = true
        };
    }
}
