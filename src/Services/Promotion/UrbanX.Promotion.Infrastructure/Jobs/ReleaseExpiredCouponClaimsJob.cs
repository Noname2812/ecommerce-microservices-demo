using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrbanX.Promotion.Application.Usecases.V1.Command;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Infrastructure.DependencyInjection.Options;

namespace UrbanX.Promotion.Infrastructure.Jobs;

public sealed class ReleaseExpiredCouponClaimsJob(
    ICouponClaimRepository couponClaimRepository,
    ISender sender,
    IOptions<ReleaseExpiredCouponClaimsJobOptions> options,
    ILogger<ReleaseExpiredCouponClaimsJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var expiredClaims = await couponClaimRepository.GetExpiredClaimedBatchAsync(
            options.Value.BatchSize,
            utcNow,
            cancellationToken);

        var releasedCount = 0;
        foreach (var claim in expiredClaims)
        {
            var result = await sender.Send(new ReleaseCouponClaimCommand(claim.Id), cancellationToken);
            if (result.IsSuccess)
            {
                releasedCount++;
                continue;
            }

            logger.LogWarning(
                "TTL release failed for expired claim ClaimId={ClaimId} ErrorCode={ErrorCode}",
                claim.Id,
                result.Error.Code);
        }

        if (releasedCount > 0)
            logger.LogInformation("TTL released {Count} expired coupon claims", releasedCount);
    }
}
