using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrbanX.Catalog.Application.Abstractions;

namespace UrbanX.Catalog.Infrastructure.Services;

public sealed class InventoryServiceClient(
    HttpClient httpClient,
    ILogger<InventoryServiceClient> logger) : IInventoryServiceClient
{
    public async Task<VariantInventoryStatus?> GetVariantInventoryStatusAsync(
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"/api/inventory/variants/{variantId}/status",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Failed to get inventory status for variant {VariantId}. Status code: {StatusCode}",
                    variantId,
                    response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<VariantInventoryStatus>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while calling Inventory service for variant {VariantId}", variantId);
            return null;
        }
    }
}
