namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// Calls Inventory reserve/release over HTTP; maps HTTP outcomes to typed exceptions.
/// </summary>
public interface IInventoryClient
{
    /// <summary>
    /// Sends idempotency key <c>{request.RequestIdempotencyKey}:inv</c> to Inventory reserve API.
    /// </summary>
    Task<ReserveResponse> ReserveAsync(ReserveRequest request, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default);
}
