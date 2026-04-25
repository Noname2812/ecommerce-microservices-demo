namespace UrbanX.Catalog.Application.Abstractions
{
    public record VariantInventoryStatus(int Quantity, bool HasActiveReservations);

    public interface IInventoryServiceClient
    {
        /// <summary>Returns null when the Inventory service is unreachable.</summary>
        Task<VariantInventoryStatus?> GetVariantInventoryStatusAsync(
            Guid variantId,
            CancellationToken cancellationToken = default);
    }
}
