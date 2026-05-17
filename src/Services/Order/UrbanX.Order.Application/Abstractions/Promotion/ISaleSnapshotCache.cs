using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Abstractions.Promotion;

/// <summary>
/// Snapshot of a sale campaign cached prior to the campaign starting.
/// Written by Promotion service's warm-up cronjob; read-only here.
/// </summary>
public sealed record CampaignSnapshot(
    Guid CampaignId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsActive);

/// <summary>
/// Tiered read-only lookup for sale campaign + sale prices.
/// Implementations should:
///   L1: in-process <c>IMemoryCache</c> (short TTL, smooths Redis traffic for hot campaigns)
///   L2: Redis (populated by Promotion service warm-up cronjob)
/// No HTTP fallback — if data is missing the campaign is treated as unavailable.
/// </summary>
public interface ISaleSnapshotCache
{
    Task<Result<CampaignSnapshot?>> GetCampaignAsync(Guid campaignId, CancellationToken ct);

    Task<Result<IReadOnlyDictionary<Guid, decimal>>> GetSalePricesAsync(
        Guid campaignId,
        CancellationToken ct);
}
