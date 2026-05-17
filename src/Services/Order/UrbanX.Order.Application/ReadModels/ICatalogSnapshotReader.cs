namespace UrbanX.Order.Application.ReadModels;

/// <summary>
/// L2 (local read model) lookup. Validators consult this before falling back to Catalog HTTP.
/// Data is populated by Catalog integration event consumers — eventual-consistent.
/// </summary>
public interface ICatalogSnapshotReader
{
    Task<IReadOnlyDictionary<Guid, CatalogSnapshotRow>> GetByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CatalogSnapshotRow>>> GetByProductIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}
