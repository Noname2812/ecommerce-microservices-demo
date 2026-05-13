using Refit;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Infrastructure.RefitApi.Inventory.Dtos;

namespace UrbanX.Order.Infrastructure.RefitApi.Inventory;

/// <summary>Refit contract for Inventory internal reservation APIs.</summary>
public interface IInventoryApi
{
    [Post("/internal/v1/reservations")]
    Task<ReserveResponse> ReserveAsync([Body] ReserveInventoryApiBody body, CancellationToken cancellationToken);

    [Delete("/internal/v1/reservations/{reservationId}")]
    Task<HttpResponseMessage> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken);
}
