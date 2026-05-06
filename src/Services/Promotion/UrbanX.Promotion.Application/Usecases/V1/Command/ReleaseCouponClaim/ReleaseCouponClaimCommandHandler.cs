using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Messaging.PlaceOrder;
using Shared.Kernel.Primitives;
using UrbanX.Promotion.Application.Abstractions;
using UrbanX.Promotion.Application.Logging;
using UrbanX.Promotion.Application.Telemetry;
using UrbanX.Promotion.Application.Usecases.V1.Errors;
using UrbanX.Promotion.Domain.Models;
using UrbanX.Promotion.Domain.Repositories;
using UrbanX.Promotion.Domain.ValueObjects;

namespace UrbanX.Promotion.Application.Usecases.V1.Command;

internal sealed class ReleaseCouponClaimCommandHandler(
    ICouponClaimRepository couponClaimRepository,
    ICouponRepository couponRepository,
    IProcessedEventRepository processedEvents,
    ICouponClaimRedisGateway couponClaimRedis,
    IPostCommitTaskQueue postCommitTasks,
    ILogger<ReleaseCouponClaimCommandHandler> logger)
    : ICommandHandler<ReleaseCouponClaimCommand>
{
    public async Task<Result> Handle(ReleaseCouponClaimCommand request, CancellationToken cancellationToken)
    {
        if (request.EventId is { } inboxEventId &&
            await processedEvents.ExistsAsync(inboxEventId, cancellationToken))
            return Result.Success();

        var result = await ExecuteReleaseAsync(request, cancellationToken);
        if (result.IsSuccess)
            StageProcessedEventIfNeeded(request.EventId);

        return result;
    }

    private async Task<Result> ExecuteReleaseAsync(ReleaseCouponClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await couponClaimRepository.GetByIdAsync(request.ClaimId, cancellationToken);
        if (claim is null)
            return Result.Failure(CouponClaimErrors.NotFound(request.ClaimId));

        if (claim.Status == CouponClaimStatus.Released)
            return Result.Success();

        if (claim.Status != CouponClaimStatus.Claimed)
            return Result.Failure(CouponClaimErrors.InvalidStatusForRelease(claim.Status));

        var now = DateTimeOffset.UtcNow;

        // Single-winner: only TryMarkReleased == 1 runs UsedQuota decrement + Redis post-commit (avoids double-decrement under concurrency).
        var updated = await couponClaimRepository.TryMarkReleasedIfClaimedAsync(
            request.ClaimId,
            now,
            cancellationToken);

        if (updated != 1)
        {
            var again = await couponClaimRepository.GetByIdAsync(request.ClaimId, cancellationToken);
            if (again is null)
                return Result.Failure(CouponClaimErrors.NotFound(request.ClaimId));

            if (again.Status == CouponClaimStatus.Released)
            {
                logger.LogInformation(
                    "Coupon claim release absorbed concurrent winner ClaimId={ClaimId} CouponCode={CouponCode}",
                    request.ClaimId,
                    again.CouponCode);
                return Result.Success();
            }

            return Result.Failure(CouponClaimErrors.InvalidStatusForRelease(again.Status));
        }

        // Rows updated (0 vs 1) is intentionally ignored: Redis restore is driven by RestoreQuotaSlotOnRelease (claim-time snapshot of quota consumption),
        // not by whether DB UsedQuota still reflects a row to decrement (drift / manual DBA edits).
        await couponRepository.TryDecrementUsedQuotaIfPositiveAsync(claim.CouponCode, cancellationToken);

        var couponCodeSnapshot = claim.CouponCode;
        var userSnapshot = claim.UserId;
        var restoreQuota = claim.RestoreQuotaSlotOnRelease;
        var claimIdForPostCommit = claim.Id;
        postCommitTasks.Enqueue(async ct =>
        {
            try
            {
                await couponClaimRedis.ReleaseClaimRedisStateAsync(couponCodeSnapshot, userSnapshot, restoreQuota, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    PromotionLogEvents.CouponClaimRedisPostCommitFailed,
                    ex,
                    "Post-commit Redis release failed — DB claim is RELEASED; user hold/quota may be stale until TTL/reconcile. ClaimId={ClaimId} CouponCode={CouponCode} UserId={UserId} RestoreQuotaSlot={RestoreQuotaSlot}. Prefer metric/alert on this EventId; safe manual step is DeleteUserHold; avoid blind INCR if quota may already reflect release.",
                    claimIdForPostCommit,
                    couponCodeSnapshot,
                    userSnapshot,
                    restoreQuota);

                PromotionMetrics.RecordCouponClaimRedisPostCommitFailure(
                    claimIdForPostCommit,
                    couponCodeSnapshot,
                    userSnapshot,
                    restoreQuota);

                throw;
            }
        });

        logger.LogInformation(
            "Coupon claim released in SQL; queued Redis cleanup post-commit ClaimId={ClaimId} CouponCode={CouponCode} UserId={UserId} RestoreQuota={RestoreQuota}",
            claim.Id,
            couponCodeSnapshot,
            userSnapshot,
            restoreQuota);

        return Result.Success();
    }

    private void StageProcessedEventIfNeeded(Guid? eventId)
    {
        if (eventId is null)
            return;

        processedEvents.StageInsert(
            new ProcessedEvent
            {
                EventId = eventId.Value,
                EventType = nameof(ICouponReleaseRequested),
                ProcessedAt = DateTimeOffset.UtcNow
            });
    }
}
