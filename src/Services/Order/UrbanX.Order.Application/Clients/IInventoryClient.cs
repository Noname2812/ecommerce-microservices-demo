namespace UrbanX.Order.Application.Clients;

public sealed record ReserveRequest(
    string RequestIdempotencyKey,
    IReadOnlyList<ReserveLineItem> Items);

public sealed record ReserveLineItem(Guid ProductId, int Quantity);

public sealed record ReserveResponse(
    Guid ReservationId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ReservedItemResponse> Items);

public sealed record ReservedItemResponse(Guid ProductId, int Quantity);

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
