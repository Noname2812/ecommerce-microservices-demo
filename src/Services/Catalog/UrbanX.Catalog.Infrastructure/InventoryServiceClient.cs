using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrbanX.Catalog.Application.Abstractions;
namespace UrbanX.Catalog.Infrastructure;
public class InventoryServiceClient : IInventoryServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryServiceClient> _logger;

    public InventoryServiceClient(HttpClient httpClient, ILogger<InventoryServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<VariantInventoryStatus?> GetVariantInventoryStatusAsync(Guid variantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/inventory/variants/{variantId}/status", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var status = JsonSerializer.Deserialize<VariantInventoryStatus>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return status;
            }
            else
            {
                _logger.LogWarning("Failed to get inventory status for variant {VariantId}. Status code: {StatusCode}", variantId, response.StatusCode);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while calling Inventory service for variant {VariantId}", variantId);
            return null;
        }
    }
}