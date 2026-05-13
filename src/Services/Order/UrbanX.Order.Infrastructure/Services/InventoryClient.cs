using System.Net;
using Refit;
using UrbanX.Order.Application.Clients;
using UrbanX.Order.Application.Exceptions;
using UrbanX.Order.Infrastructure.RefitApi.Inventory;
using UrbanX.Order.Infrastructure.RefitApi.Inventory.Dtos;

namespace UrbanX.Order.Infrastructure.Services;

public sealed class InventoryClient(IInventoryApi api) : IInventoryClient
{
    public async Task<ReserveResponse> ReserveAsync(ReserveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var idempotencyKey = $"{request.RequestIdempotencyKey}:inv";
        var body = new ReserveInventoryApiBody(
            idempotencyKey,
            request.Items.Select(i => new ReserveInventoryApiLineItem(i.ProductId, i.Quantity)).ToList());

        try
        {
            return await api.ReserveAsync(body, cancellationToken);
        }
        catch (ApiException ex)
        {
            throw MapReserveApiException(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InventoryUnavailableException("Inventory reserve request timed out.", ex);
        }
    }

    public async Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await api.ReleaseAsync(reservationId, cancellationToken);
            if ((int)response.StatusCode >= 500)
            {
                throw new InventoryUnavailableException(
                    $"Inventory release failed with status {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await TryReadContentAsync(response, cancellationToken).ConfigureAwait(false);
                throw new InventoryValidationException(
                    detail ?? $"Inventory release failed with status {(int)response.StatusCode}.");
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InventoryUnavailableException("Inventory release request timed out.", ex);
        }
    }

    private static Exception MapReserveApiException(ApiException ex)
    {
        var code = ex.StatusCode;

        if (code == HttpStatusCode.Conflict)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content!;
            return new OutOfStockException(detail);
        }

        if ((int)code >= 500)
        {
            var msg = string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content!;
            return new InventoryUnavailableException(msg, ex);
        }

        if (code != default && (int)code >= 400)
        {
            var msg = string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content!;
            return new InventoryValidationException(msg, ex);
        }

        return new InventoryUnavailableException(ex.Message, ex);
    }

    private static async Task<string?> TryReadContentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
