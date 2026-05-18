using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Services;

public interface IPendingOrderSlotService
{
    Task<Result> TryAcquireAsync(Guid userId, string orderType, CancellationToken ct);

    Task ReleaseAsync(Guid userId, CancellationToken ct);
}
