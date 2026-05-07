using Refit;

namespace UrbanX.Order.Infrastructure.Services;

/// <summary>
/// Refit contract for Inventory internal reservation APIs.
/// </summary>
public interface IInventoryApi
{
    [Post("/internal/v1/reservations")]
    Task<ReserveResponse> ReserveAsync([Body] ReserveInventoryApiBody body, CancellationToken cancellationToken);

    [Delete("/internal/v1/reservations/{reservationId}")]
    Task<HttpResponseMessage> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);
}

/// <summary>
/// JSON body aligned with Inventory reserve command (idempotency key + line items).
/// </summary>
public sealed record ReserveInventoryApiBody(string IdempotencyKey, IReadOnlyList<ReserveInventoryApiLineItem> Items);

public sealed record ReserveInventoryApiLineItem(Guid ProductId, int Quantity);
