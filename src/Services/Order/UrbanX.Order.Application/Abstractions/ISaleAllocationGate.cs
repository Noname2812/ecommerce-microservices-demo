using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Abstractions;

public interface ISaleAllocationGate
{
    Task<Result<string>> TryReserveAsync(
        Guid campaignId,
        Guid userId,
        int totalQty,
        CancellationToken ct);

    Task ReleaseAsync(
        string quotaKey,
        Guid campaignId,
        Guid userId,
        int totalQty,
        CancellationToken ct);
}
